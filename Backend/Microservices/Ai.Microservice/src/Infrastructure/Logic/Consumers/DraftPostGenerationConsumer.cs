using System.Text;
using System.Text.Json;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
using Application.Recommendations.Commands;
using Application.Recommendations.Queries;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.Resources;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Recommendations;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

/// <summary>
/// End-to-end async draft-post generation:
///   1. Auto-index latest posts (skip-if-unchanged via fingerprint registry)
///   2. RAG multimodal query (text context + image references)
///   3. Caption generation (gpt-4o-mini multimodal, references attached as image_url parts)
///   4. Image generation (gpt-5.4-image-2 multimodal, same references for visual style)
///   5. Upload generated image to S3 via User microservice (data URL)
///   6. Create PostBuilder + Post via existing CreatePostCommand
///   7. Update DraftPostTask + publish notification
/// </summary>
public sealed class DraftPostGenerationConsumer : IConsumer<GenerateDraftPostStarted>
{
    private const string CaptionSystemPrompt =
        "You are a social-media caption writer. You see (a) the user's topic for the next post, " +
        "(b) recent post captions from the same account so you can match voice and style, and " +
        "(c) a few reference images from past posts. Write ONE caption for the next post in the SAME " +
        "language and tone as the past captions. Match emoji density and hashtag style. " +
        "Output the caption only — no preface, no numbering, no markdown headings.";

    private const string ImageSystemPrompt =
        "You are an image-generation assistant for social media. Produce ONE image that fits the " +
        "user's topic AND matches the visual style of the reference images attached (color palette, " +
        "lighting, composition, mood, subject framing). Output an image, not text.";

    private readonly IMediator _mediator;
    private readonly IDraftPostTaskRepository _taskRepository;
    private readonly IPostRepository _postRepository;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly IImageGenerationClient _imageGenClient;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<DraftPostGenerationConsumer> _logger;

    public DraftPostGenerationConsumer(
        IMediator mediator,
        IDraftPostTaskRepository taskRepository,
        IPostRepository postRepository,
        IMultimodalLlmClient multimodalLlm,
        IImageGenerationClient imageGenClient,
        IUserResourceService userResourceService,
        ILogger<DraftPostGenerationConsumer> logger)
    {
        _mediator = mediator;
        _taskRepository = taskRepository;
        _postRepository = postRepository;
        _multimodalLlm = multimodalLlm;
        _imageGenClient = imageGenClient;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GenerateDraftPostStarted> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "DraftPost: starting CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
            msg.CorrelationId, msg.UserId, msg.SocialMediaId);

        var task = await _taskRepository.GetByCorrelationIdForUpdateAsync(msg.CorrelationId, ct);
        if (task is null)
        {
            _logger.LogWarning("DraftPost: task not found for CorrelationId={CorrelationId}", msg.CorrelationId);
            return;
        }

        try
        {
            task.Status = DraftPostTaskStatuses.Processing;
            task.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            await _taskRepository.SaveChangesAsync(ct);

            // Step 1 — auto-index. Existing skip-if-unchanged logic ensures only new/changed
            // posts hit RAG; unchanged ones are no-ops.
            var indexMaxPosts = msg.MaxRagPosts > 0 ? msg.MaxRagPosts : 30;
            _logger.LogDebug("DraftPost {Id}: indexing posts (max={Max})...", task.Id, indexMaxPosts);
            var indexResult = await _mediator.Send(
                new IndexSocialAccountPostsCommand(msg.UserId, msg.SocialMediaId, indexMaxPosts), ct);
            if (indexResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Indexing failed: {indexResult.Error.Code} {indexResult.Error.Description}");
            }
            _logger.LogInformation(
                "DraftPost {Id}: indexed total={Total} new={New} updated={Updated} unchanged={Unchanged}",
                task.Id,
                indexResult.Value.TotalPostsScanned,
                indexResult.Value.NewPosts,
                indexResult.Value.UpdatedPosts,
                indexResult.Value.UnchangedPosts);

            // Step 2 — RAG multimodal query. Reuses the same retrieval as /query: text context
            // + visual hits with image URLs.
            _logger.LogDebug("DraftPost {Id}: querying RAG...", task.Id);
            var queryResult = await _mediator.Send(
                new QueryAccountRecommendationsQuery(
                    msg.UserId, msg.SocialMediaId, msg.UserPrompt, msg.TopK), ct);
            if (queryResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"RAG query failed: {queryResult.Error.Code} {queryResult.Error.Description}");
            }
            var rag = queryResult.Value;

            var refsWithImages = rag.References
                .Where(r => !string.IsNullOrWhiteSpace(r.ImageUrl))
                .Take(msg.MaxReferenceImages)
                .ToList();
            var topImageUrls = refsWithImages.Select(r => r.ImageUrl!).ToList();

            // Step 3 — caption generation (gpt-4o-mini multimodal)
            _logger.LogDebug(
                "DraftPost {Id}: generating caption with {RefCount} ref images...",
                task.Id, topImageUrls.Count);
            var captionUserText = BuildCaptionUserText(msg.UserPrompt, rag, topImageUrls.Count);
            var caption = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: CaptionSystemPrompt,
                    UserText: captionUserText,
                    ReferenceImageUrls: topImageUrls),
                ct);
            caption = (caption ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(caption))
            {
                throw new InvalidOperationException("Caption generation returned empty content.");
            }

            // Step 4 — image generation (gpt-5.4-image-2 multimodal)
            _logger.LogDebug("DraftPost {Id}: generating image...", task.Id);
            var imagePrompt = BuildImagePrompt(msg.UserPrompt);
            var imageResult = await _imageGenClient.GenerateImageAsync(
                new ImageGenerationRequest(
                    Prompt: imagePrompt,
                    ReferenceImageUrls: topImageUrls,
                    SystemPrompt: ImageSystemPrompt),
                ct);

            // Step 5 — upload generated image to S3. The User microservice's
            // CreateResourcesFromUrlsAsync handles `data:` URLs by decoding base64 server-side.
            _logger.LogDebug("DraftPost {Id}: uploading generated image to S3...", task.Id);
            var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                userId: msg.UserId,
                urls: new[] { imageResult.DataUrl },
                status: "generated",
                resourceType: "image",
                cancellationToken: ct,
                workspaceId: msg.WorkspaceId,
                provenance: new ResourceProvenanceMetadata(
                    OriginKind: ResourceOriginKinds.AiGenerated,
                    OriginChatSessionId: null,
                    OriginChatId: null));

            if (uploadResult.IsFailure || uploadResult.Value.Count == 0)
            {
                throw new InvalidOperationException(
                    $"S3 upload failed: {uploadResult.Error?.Code} {uploadResult.Error?.Description}");
            }
            var uploaded = uploadResult.Value[0];

            // Step 6 — persist as a STANDALONE draft Post (no PostBuilder).
            // We bypass CreatePostCommand here because that command always creates or
            // upserts a PostBuilder; for AI-generated draft recommendations we want a
            // bare draft row the user can promote later, not pre-bound to a builder.
            _logger.LogDebug("DraftPost {Id}: creating standalone draft Post (no PostBuilder)...", task.Id);
            var content = new PostContent
            {
                Content = caption,
                ResourceList = new List<string> { uploaded.ResourceId.ToString() },
                PostType = "posts",
            };
            var draftPost = new Post
            {
                Id = Guid.CreateVersion7(),
                UserId = msg.UserId,
                WorkspaceId = msg.WorkspaceId,
                ChatSessionId = null,
                SocialMediaId = msg.SocialMediaId,
                PostBuilderId = null,
                Platform = null,
                Title = null,
                Content = content,
                Status = "draft",
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
                UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow,
            };
            await _postRepository.AddAsync(draftPost, ct);
            await _postRepository.SaveChangesAsync(ct);

            // Step 7 — mark task completed + notify
            task.Status = DraftPostTaskStatuses.Completed;
            task.ResultPostBuilderId = null;
            task.ResultPostId = draftPost.Id;
            task.ResultResourceId = uploaded.ResourceId;
            task.ResultPresignedUrl = uploaded.PresignedUrl;
            task.ResultCaption = caption;
            task.ResultReferencesJson = SerializeReferences(rag.References);
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            await _taskRepository.SaveChangesAsync(ct);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    msg.UserId,
                    NotificationTypes.AiDraftPostGenerationCompleted,
                    "Draft post is ready",
                    "Your AI-generated draft post (caption + image) is ready.",
                    new
                    {
                        correlationId = task.CorrelationId,
                        socialMediaId = task.SocialMediaId,
                        postId = task.ResultPostId,
                        resourceId = task.ResultResourceId,
                        presignedUrl = task.ResultPresignedUrl,
                        caption = task.ResultCaption,
                    },
                    createdAt: task.CompletedAt,
                    source: NotificationSourceConstants.Creator),
                ct);

            _logger.LogInformation(
                "DraftPost {Id}: completed CorrelationId={CorrelationId} PostId={PostId} ResourceId={ResourceId}",
                task.Id, task.CorrelationId, task.ResultPostId, task.ResultResourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DraftPost {Id}: failed CorrelationId={CorrelationId}", task.Id, task.CorrelationId);

            task.Status = DraftPostTaskStatuses.Failed;
            task.ErrorCode = ex.GetType().Name;
            task.ErrorMessage = ex.Message;
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            try
            {
                await _taskRepository.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "DraftPost {Id}: failed to persist Failed status", task.Id);
            }

            try
            {
                await context.Publish(
                    NotificationRequestedEventFactory.CreateForUser(
                        msg.UserId,
                        NotificationTypes.AiDraftPostGenerationFailed,
                        "Draft post generation failed",
                        "Your AI draft post could not be generated. Please try again.",
                        new
                        {
                            correlationId = task.CorrelationId,
                            socialMediaId = task.SocialMediaId,
                            errorCode = task.ErrorCode,
                            errorMessage = task.ErrorMessage,
                        },
                        createdAt: task.CompletedAt,
                        source: NotificationSourceConstants.Creator),
                    ct);
            }
            catch (Exception notifyEx)
            {
                _logger.LogError(notifyEx, "DraftPost {Id}: failed to publish failure notification", task.Id);
            }
        }
    }

    private static string BuildCaptionUserText(
        string userPrompt,
        Application.Recommendations.Queries.AccountRecommendationsAnswer rag,
        int attachedImageCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"User's topic for the next post: {userPrompt}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(rag.Answer))
        {
            sb.AppendLine("Retrieved RAG recommendation summary:");
            sb.AppendLine(rag.Answer);
            sb.AppendLine();
        }
        var captionSamples = rag.References
            .Where(r => !string.IsNullOrWhiteSpace(r.Caption))
            .Take(6)
            .ToList();
        if (captionSamples.Count > 0)
        {
            sb.AppendLine("Recent past captions from this account (for voice/style):");
            for (var i = 0; i < captionSamples.Count; i++)
            {
                var r = captionSamples[i];
                var snippet = (r.Caption ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
                if (snippet.Length > 240) snippet = snippet[..240] + "...";
                sb.AppendLine($"[{i + 1}] postId={r.PostId} caption=\"{snippet}\"");
            }
            sb.AppendLine();
        }
        if (attachedImageCount > 0)
        {
            sb.AppendLine($"The next {attachedImageCount} attached image(s) are reference images from past posts. Use them as visual context.");
        }
        return sb.ToString();
    }

    private static string BuildImagePrompt(string userPrompt)
        => $"Generate an image for a social media post on this topic: {userPrompt}.\n\n" +
           "Match the visual style of the attached reference images: same palette, same lighting, " +
           "similar composition. Keep it brand-consistent.";

    private static string SerializeReferences(IReadOnlyList<RecommendationReference> references)
    {
        try
        {
            return JsonSerializer.Serialize(references, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        }
        catch
        {
            return "[]";
        }
    }
}
