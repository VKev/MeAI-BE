using Application.Abstractions.Formulas;
using Application.Usage;
using Application.Usage.Models;
using Application.Abstractions;
using Application.Abstractions.Agents;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Application.Abstractions.Kie;
using Application.Abstractions.Facebook;
using Application.Abstractions.Feed;
using Application.Abstractions.Gemini;
using Application.Abstractions.Instagram;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Workspaces;
using Application.Abstractions.TikTok;
using Application.Abstractions.Threads;
using Application.Posts;
using Application.PublishingSchedules;
using Domain.Repositories;
using Infrastructure.Logic.Consumers;
using Infrastructure.Logic.Agents;
using Infrastructure.Logic.Automation;
using Infrastructure.Logic.ApiCredentials;
using Infrastructure.Logic.Configs;
using Infrastructure.Logic.Facebook;
using Infrastructure.Logic.Feed;
using Infrastructure.Logic.Formulas;
using Infrastructure.Logic.Gemini;
using Infrastructure.Logic.Instagram;
using Infrastructure.Logic.Rag;
using Infrastructure.Logic.Threads;
using Infrastructure.Logic.Resources;
using Infrastructure.Logic.SocialMedias;
using Infrastructure.Logic.TikTok;
using Infrastructure.Logic.Workspaces;
using Infrastructure.Repositories;
using Infrastructure.Logic.Sagas;
using Infrastructure.Logic.Seeding;
using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedLibrary.Authentication;
using SharedLibrary.Configs;
using SharedLibrary.Grpc.FeedAnalytics;
using SharedLibrary.Grpc.FeedPosts;
using SharedLibrary.Grpc.UserResources;
using StackExchange.Redis;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddSingleton<ApiCredentialCryptoService>();
            services.AddSingleton<IApiCredentialProvider, ApiCredentialProvider>();
            services.AddScoped<ApiCredentialSyncSeeder>();
            services.AddHttpClient<IVeoVideoService, VeoVideoService>();
            services.AddHttpClient<IKieImageService, KieImageService>();
            services.AddHttpClient<IKieAccountService, KieAccountService>();
            services.AddHttpClient<IKieFallbackCallbackService, KieFallbackCallbackService>();
            services.AddSingleton<IAiFallbackTemplateService, AiFallbackTemplateService>();
            services.AddHttpClient("Gemini");
            services.AddHttpClient("KieChat");
            services.AddScoped<Infrastructure.Logic.Kie.KieResponsesClient>();
            services.AddHttpClient("Facebook");
            services.AddHttpClient("Instagram");
            services.AddHttpClient("TikTok");
            services.AddHttpClient("WebSearchContent", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MeAIWebSearch/1.0)");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,vi;q=0.8");
            });
            // Caption generation runs through Kie's GPT-5.4 Responses API. GeminiCaptionService
            // stays registered as a concrete class for future fallback / A-B; the interface
            // binding points at the Kie-backed implementation.
            services.AddScoped<IGeminiCaptionService, Infrastructure.Logic.Kie.KieCaptionService>();
            services.AddScoped<GeminiCaptionService>();
            services.AddScoped<IFormulaTemplateRenderer, FormulaTemplateRenderer>();
            services.AddScoped<IFormulaGenerationService, FormulaGenerationService>();
            services.AddScoped<IAgentWebSearchService, AgentWebSearchService>();
            services.AddScoped<IWebSearchEnrichmentService, WebSearchEnrichmentService>();
            services.AddScoped<IAgenticRuntimeContentService, AgenticRuntimeContentService>();
            services.AddScoped<IFacebookContentService, FacebookContentService>();
            services.AddScoped<IGeminiContentModerationService, Infrastructure.Logic.Kie.KieContentModerationService>();
            services.AddScoped<IAgentChatService, GeminiAgentChatService>();
            services.AddScoped<IChatWebPostService, ChatWebPostService>();
            services.AddScoped<IFacebookPublishService, FacebookPublishService>();
            services.AddScoped<IInstagramPublishService, InstagramPublishService>();
            services.AddScoped<IInstagramContentService, InstagramContentService>();
            services.AddScoped<ITikTokPublishService, TikTokPublishService>();
            services.AddScoped<ITikTokContentService, TikTokContentService>();
            services.AddHttpClient("Threads");
            services.AddScoped<IThreadsPublishService, ThreadsPublishService>();
            services.AddScoped<IThreadsContentService, ThreadsContentService>();

            services.AddSingleton(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var ingest = configuration["Rag:IngestQueue"]
                             ?? configuration["RAG_INGEST_QUEUE"]
                             ?? "meai.rag.ingest";
                var query = configuration["Rag:QueryQueue"]
                            ?? configuration["RAG_QUERY_QUEUE"]
                            ?? "meai.rag.query";
                var timeoutSeconds = int.TryParse(
                    configuration["Rag:RpcTimeoutSeconds"] ?? configuration["RAG_RPC_TIMEOUT_SECONDS"],
                    out var seconds)
                    ? seconds
                    : 30;
                var grpcUrl = configuration["Rag:GrpcUrl"]
                              ?? configuration["RAG_GRPC_URL"]
                              ?? "http://rag-microservice:5006";
                var grpcIngestTimeoutSeconds = int.TryParse(
                    configuration["Rag:GrpcIngestTimeoutSeconds"] ?? configuration["RAG_GRPC_INGEST_TIMEOUT_SECONDS"],
                    out var grpcSeconds)
                    ? grpcSeconds
                    : 300;
                var waitReadyTimeoutSeconds = int.TryParse(
                    configuration["Rag:WaitReadyTimeoutSeconds"] ?? configuration["RAG_WAIT_READY_TIMEOUT_SECONDS"],
                    out var waitSeconds)
                    ? waitSeconds
                    : 1800;
                var s3PublicBaseUrl = configuration["Rag:S3PublicBaseUrl"]
                                      ?? configuration["RAG_S3_PUBLIC_BASE_URL"]
                                      ?? configuration["S3:PublicBaseUrl"]
                                      ?? configuration["VIDEORAG_S3_PUBLIC_BASE_URL"]
                                      ?? "https://static.vkev.me";
                return new RagOptions
                {
                    IngestQueue = ingest,
                    QueryQueue = query,
                    RpcTimeout = TimeSpan.FromSeconds(timeoutSeconds),
                    GrpcUrl = grpcUrl,
                    GrpcIngestTimeout = TimeSpan.FromSeconds(grpcIngestTimeoutSeconds),
                    S3PublicBaseUrl = s3PublicBaseUrl,
                    WaitReadyTimeout = TimeSpan.FromSeconds(waitReadyTimeoutSeconds),
                };
            });

            // gRPC client to rag-microservice for synchronous batch ingest. The
            // existing AMQP-RPC query path stays on RabbitMQ; only the synchronous
            // ingest path uses gRPC.
            services.AddGrpcClient<SharedLibrary.Grpc.Rag.RagIngestService.RagIngestServiceClient>((sp, options) =>
            {
                var ragOpts = sp.GetRequiredService<RagOptions>();
                options.Address = new Uri(ragOpts.GrpcUrl);
            });
            services.AddSingleton<IRagClient, RabbitMqRagClient>();

            services.AddSingleton(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var timeoutSeconds = int.TryParse(
                    configuration["Rag:MultimodalAnswerTimeoutSeconds"]
                    ?? configuration["RAG_MULTIMODAL_ANSWER_TIMEOUT_SECONDS"],
                    out var s) ? s : 60;
                var webMax = int.TryParse(
                    configuration["Rag:WebSearchMaxResults"]
                    ?? configuration["RAG_WEB_SEARCH_MAX_RESULTS"],
                    out var w) ? w : 5;
                var webEnabled = !string.Equals(
                    configuration["Rag:WebSearchEnabled"]
                    ?? configuration["RAG_WEB_SEARCH_ENABLED"]
                    ?? "true", "false", StringComparison.OrdinalIgnoreCase);
                return new MultimodalLlmOptions
                {
                    BaseUrl = configuration["Rag:MultimodalLlmBaseUrl"]
                              ?? configuration["RAG_MULTIMODAL_LLM_BASE_URL"]
                              ?? "https://openrouter.ai/api/v1",
                    ApiKey = configuration["Rag:MultimodalLlmApiKey"]
                             ?? configuration["RAG_MULTIMODAL_LLM_API_KEY"]
                             ?? string.Empty,
                    Model = configuration["Rag:MultimodalLlmModel"]
                            ?? configuration["RAG_MULTIMODAL_LLM_MODEL"]
                            ?? "openai/gpt-4o-mini",
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                    WebSearchEnabled = webEnabled,
                    WebSearchMaxResults = webMax,
                };
            });

            // Brave Search backend for the `web_search` tool exposed by the
            // OpenRouter multimodal client. If no API key is configured, the
            // BraveSearchClient gracefully returns empty results — model still
            // sees the tool but every invocation returns nothing.
            services.AddSingleton(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new Infrastructure.Logic.Search.BraveSearchOptions
                {
                    BaseUrl = configuration["Rag:BraveSearchBaseUrl"]
                              ?? configuration["RAG_BRAVE_SEARCH_BASE_URL"]
                              ?? "https://api.search.brave.com/res/v1/web/search",
                    ApiKey = configuration["Rag:BraveSearchApiKey"]
                             ?? configuration["RAG_BRAVE_SEARCH_API_KEY"]
                             ?? string.Empty,
                    Timeout = TimeSpan.FromSeconds(10),
                };
            });
            services.AddHttpClient<Application.Abstractions.Search.IWebSearchClient,
                                   Infrastructure.Logic.Search.BraveSearchClient>();

            // Brave image search — separate endpoint, same subscription key. Used at
            // draft-generation time to fetch a fresh real-world reference image of the
            // topic so the image-gen model has a concrete subject anchor (e.g. an actual
            // DJI Osmo product shot) alongside the brand's past-post images.
            services.AddSingleton(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new Infrastructure.Logic.Search.BraveImageSearchOptions
                {
                    BaseUrl = configuration["Rag:BraveImageSearchBaseUrl"]
                              ?? configuration["RAG_BRAVE_IMAGE_SEARCH_BASE_URL"]
                              ?? "https://api.search.brave.com/res/v1/images/search",
                    ApiKey = configuration["Rag:BraveImageSearchApiKey"]
                             ?? configuration["RAG_BRAVE_IMAGE_SEARCH_API_KEY"]
                             // Fall back to the text-search key by default — same Brave
                             // account, same subscription. The override exists if the
                             // user wants to point image search at a different plan/key.
                             ?? configuration["Rag:BraveSearchApiKey"]
                             ?? configuration["RAG_BRAVE_SEARCH_API_KEY"]
                             ?? string.Empty,
                    Country = configuration["Rag:BraveImageSearchCountry"]
                              ?? configuration["RAG_BRAVE_IMAGE_SEARCH_COUNTRY"]
                              ?? "us",
                    SafeSearch = configuration["Rag:BraveImageSearchSafe"]
                                 ?? configuration["RAG_BRAVE_IMAGE_SEARCH_SAFE"]
                                 ?? "strict",
                    Timeout = TimeSpan.FromSeconds(10),
                };
            });
            services.AddHttpClient<Application.Abstractions.Search.IImageSearchClient,
                                   Infrastructure.Logic.Search.BraveImageSearchClient>();

            // Jina-reranker-m0 — true multimodal cross-encoder. Each candidate is sent
            // as either {"image": url} (Jina fetches and scores pixel content) or
            // {"text": "..."}; image-and-text combined collapses to text-only scoring,
            // so we pick one field per candidate. Verified against the live API:
            // unlike Cohere /v2/rerank (text-only despite accepting image_url field),
            // Jina genuinely fetches images and produces pixel-aware scores.
            services.AddSingleton(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new RerankOptions
                {
                    BaseUrl = configuration["Rag:RerankBaseUrl"]
                              ?? configuration["RAG_RERANK_BASE_URL"]
                              ?? "https://api.jina.ai/v1/rerank",
                    ApiKey = configuration["Rag:RerankApiKey"]
                             ?? configuration["RAG_RERANK_API_KEY"]
                             ?? string.Empty,
                    Model = configuration["Rag:RerankModel"]
                            ?? configuration["RAG_RERANK_MODEL"]
                            ?? "jina-reranker-m0",
                    Timeout = TimeSpan.FromSeconds(20),
                };
            });
            services.AddHttpClient<Application.Abstractions.Rag.IRerankClient,
                                   Infrastructure.Logic.Rag.JinaRerankClient>();

            services.AddHttpClient<IMultimodalLlmClient, OpenRouterMultimodalLlmClient>();

            services.AddSingleton(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var timeoutSeconds = int.TryParse(
                    configuration["Rag:ImageGenTimeoutSeconds"]
                    ?? configuration["RAG_IMAGE_GEN_TIMEOUT_SECONDS"],
                    out var s) ? s : 300;
                return new ImageGenerationOptions
                {
                    BaseUrl = configuration["Rag:ImageGenBaseUrl"]
                              ?? configuration["RAG_IMAGE_GEN_BASE_URL"]
                              ?? "https://openrouter.ai/api/v1",
                    ApiKey = configuration["Rag:ImageGenApiKey"]
                             ?? configuration["RAG_IMAGE_GEN_API_KEY"]
                             ?? configuration["Rag:MultimodalLlmApiKey"]
                             ?? string.Empty,
                    Model = configuration["Rag:ImageGenModel"]
                            ?? configuration["RAG_IMAGE_GEN_MODEL"]
                            ?? "openai/gpt-5.4-image-2",
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                };
            });
            services.AddHttpClient<IImageGenerationClient, OpenRouterImageGenerationClient>();

            services.AddScoped<IDraftPostTaskRepository, DraftPostTaskRepository>();
            services.AddScoped<IRecommendPostRepository, RecommendPostRepository>();

            // Query rewriter (single LLM call up-front; outputs feed every retrieval +
            // rerank query downstream). See `Application/Recommendations/Services/QueryRewriter`.
            services.AddScoped<Application.Recommendations.Services.IQueryRewriter,
                              Application.Recommendations.Services.QueryRewriter>();

            services.AddGrpcClient<UserResourceService.UserResourceServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IUserResourceService, UserResourceGrpcService>();
            services.AddScoped<IAiGenerationStorageEstimator, AiGenerationStorageEstimator>();
            services.AddScoped<IUserConfigService, UserConfigGrpcService>();

            services.AddGrpcClient<UserSocialMediaService.UserSocialMediaServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IUserSocialMediaService, UserSocialMediaGrpcService>();

            services.AddGrpcClient<UserWorkspaceService.UserWorkspaceServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IUserWorkspaceService, UserWorkspaceGrpcService>();

            services.AddGrpcClient<SharedLibrary.Grpc.UserBilling.UserBillingService.UserBillingServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<Application.Abstractions.Billing.IBillingClient, Infrastructure.Logic.Billing.BillingGrpcClient>();

            services.AddGrpcClient<FeedAnalyticsService.FeedAnalyticsServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["FeedService:GrpcUrl"]
                              ?? configuration["FeedService__GrpcUrl"]
                              ?? "http://feed-microservice:5008";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IFeedAnalyticsService, FeedAnalyticsGrpcClient>();

            services.AddGrpcClient<FeedPostPublishService.FeedPostPublishServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["FeedService:GrpcUrl"]
                              ?? configuration["FeedService__GrpcUrl"]
                              ?? "http://feed-microservice:5008";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IFeedPostPublishService, FeedPostPublishGrpcClient>();

            services.AddScoped<IVideoTaskRepository, VideoTaskRepository>();
            services.AddScoped<IImageTaskRepository, ImageTaskRepository>();
            services.AddScoped<IAiUsageTimingResolver, AiUsageTimingResolver>();
            services.AddScoped<IAiSpendRecordRepository, AiSpendRecordRepository>();
            services.AddScoped<IPromptFormulaTemplateRepository, PromptFormulaTemplateRepository>();
            services.AddScoped<IFormulaGenerationLogRepository, FormulaGenerationLogRepository>();
            services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
            services.AddScoped<IChatRepository, ChatRepository>();
            services.AddScoped<SampleDataSeeder>();
            services.AddScoped<CoinPricingSeeder>();
            services.AddScoped<Application.Billing.ICoinPricingService, Infrastructure.Logic.Billing.CoinPricingService>();
            services.AddScoped<IPostBuilderRepository, PostBuilderRepository>();
            services.AddScoped<IPostRepository, PostRepository>();
            services.AddScoped<IPublishingScheduleRepository, PublishingScheduleRepository>();
            services.AddScoped<IPostPublicationRepository, PostPublicationRepository>();
            services.AddScoped<IPostMetricSnapshotRepository, PostMetricSnapshotRepository>();
            services.AddScoped<ICoinPricingRepository, CoinPricingRepository>();
            services.AddScoped<PostResponseBuilder>();
            services.AddScoped<PublishingScheduleResponseBuilder>();
            services.AddScoped<ScheduledPostDispatchService>();
            services.AddScoped<AgenticPublishingScheduleDispatchService>();
            services.AddScoped<ResourceProvenanceBackfillService>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddHostedService<ScheduledPostPublishingWorker>();
            services.AddHostedService<AgenticPublishingScheduleWorker>();

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Application.AssemblyReference).Assembly));

            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var env = sp.GetRequiredService<EnvironmentConfig>();
                var options = new ConfigurationOptions
                {
                    AbortOnConnectFail = false
                };
                options.EndPoints.Add(env.RedisHost, env.RedisPort);
                if (!string.IsNullOrWhiteSpace(env.RedisPassword))
                {
                    options.Password = env.RedisPassword;
                }
                return ConnectionMultiplexer.Connect(options);
            });

            services.AddMassTransit(x =>
            {
                x.AddConsumer<SubmitVideoTaskConsumer>();
                x.AddConsumer<ExtendVideoTaskConsumer>();
                x.AddConsumer<VideoCompletedConsumer>();
                x.AddConsumer<VideoFailedConsumer>();

                // Image generation consumers
                x.AddConsumer<SubmitImageTaskConsumer>();
                x.AddConsumer<SubmitImageReframeConsumer>();
                x.AddConsumer<ImageCompletedConsumer>();
                x.AddConsumer<ImageFailedConsumer>();

                // Publish-to-target consumer (async per-target post publishing)
                x.AddConsumer<PublishToTargetConsumer>();
                x.AddConsumer<UnpublishFromTargetConsumer>();
                x.AddConsumer<UpdatePublishedTargetConsumer>();
                x.AddConsumer<SyncSocialMediaPostsConsumer>();
                x.AddConsumer<SocialMediaUnlinkedConsumer>();

                // Async draft-post generation: index → RAG query → caption → image → S3 → PostBuilder → notify
                x.AddConsumer<DraftPostGenerationConsumer>();

                // Async improve-existing-post generation: anchored on the original post,
                // conditional caption / image regen, persists to RecommendPost row only.
                x.AddConsumer<RecommendPostGenerationConsumer>();

                x.AddSagaStateMachine<VideoTaskStateMachine, VideoTaskState>()
                    .RedisRepository(r =>
                    {
                        r.KeyPrefix = "video-saga:";
                        r.ConnectionFactory(provider => provider.GetRequiredService<IConnectionMultiplexer>());
                    });

                x.AddSagaStateMachine<ImageTaskStateMachine, ImageTaskState>()
                    .RedisRepository(r =>
                    {
                        r.KeyPrefix = "image-saga:";
                        r.ConnectionFactory(provider => provider.GetRequiredService<IConnectionMultiplexer>());
                    });

                x.UsingRabbitMq((context, cfg) =>
                {
                    var env = context.GetRequiredService<EnvironmentConfig>();

                    if (env.IsRabbitMqCloud && !string.IsNullOrEmpty(env.RabbitMqUrl))
                    {
                        cfg.Host(new Uri(env.RabbitMqUrl));
                    }
                    else
                    {
                        cfg.Host(env.RabbitMqHost, (ushort)env.RabbitMqPort, "/", h =>
                        {
                            h.Username(env.RabbitMqUser);
                            h.Password(env.RabbitMqPassword);
                        });
                    }

                    cfg.UseMessageScheduler(new Uri("queue:video-scheduler"));

                    cfg.ConfigureEndpoints(context);
                });
            });

            return services;
        }
    }
}
