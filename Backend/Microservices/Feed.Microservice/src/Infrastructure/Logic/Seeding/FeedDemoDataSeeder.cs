using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.Seeding;

public sealed partial class FeedDemoDataSeeder
{
    private const string RuntimeDirectoryName = "runtime";
    private const string UserStateFileName = "users.state.json";
    private const string FeedStateFileName = "feed.state.json";
    private static readonly TimeSpan UserStateWaitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UserStatePollInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MyDbContext _dbContext;
    private readonly FeedSeedOptions _options;
    private readonly ILogger<FeedDemoDataSeeder> _logger;

    public FeedDemoDataSeeder(
        MyDbContext dbContext,
        IOptions<FeedSeedOptions> options,
        ILogger<FeedDemoDataSeeder> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var dataRoot = ResolveDataRoot(_options.DataRoot);
        var runtimeDirectory = Path.Combine(dataRoot, RuntimeDirectoryName);
        var userStatePath = Path.Combine(runtimeDirectory, UserStateFileName);
        var feedStatePath = Path.Combine(runtimeDirectory, FeedStateFileName);
        Directory.CreateDirectory(runtimeDirectory);

        if (!_options.Enabled)
        {
            _logger.LogInformation("Feed demo data seed skipped: FeedSeed:Enabled is false.");
            return;
        }

        if (await HasExistingFeedDataAsync(cancellationToken))
        {
            _logger.LogInformation("Feed demo data seed skipped: feed data is not empty.");
            return;
        }

        if (!await WaitForUserStateAsync(userStatePath, cancellationToken))
        {
            _logger.LogWarning(
                "Feed demo data seed skipped: user seed state was not found at {StatePath} after waiting {TimeoutSeconds} seconds.",
                userStatePath,
                UserStateWaitTimeout.TotalSeconds);
            return;
        }

        var userState = await LoadUserStateAsync(userStatePath, cancellationToken);
        if (userState is null || userState.Users.Count == 0)
        {
            _logger.LogWarning("Feed demo data seed skipped: user seed state is empty.");
            return;
        }

        var usersByUsername = userState.Users.ToDictionary(item => item.Username, StringComparer.OrdinalIgnoreCase);
        var resourcesByUsername = userState.Resources
            .GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var follows = BuildFollowGraph(usersByUsername);
        var hashtags = new Dictionary<string, Hashtag>(StringComparer.OrdinalIgnoreCase);
        var posts = new List<Post>();
        var postHashtags = new List<PostHashtag>();
        var comments = new List<Comment>();
        var postLikes = new List<PostLike>();
        var feedStatePosts = new List<FeedSeedPostState>();
        var feedStateComments = new List<FeedSeedCommentState>();

        var now = DateTime.UtcNow;
        var postPlans = BuildPostPlans(usersByUsername, resourcesByUsername);

        foreach (var plan in postPlans.Select((value, index) => (value, index)))
        {
            var createdAt = now.AddMinutes(-(postPlans.Count - plan.index));
            var postId = CreateDeterministicGuid($"feed-seed:post:{plan.value.Slug}");
            var normalizedContent = NormalizeOptionalText(plan.value.Content);
            var hashtagNames = ExtractHashtags(normalizedContent);

            var post = new Post
            {
                Id = postId,
                UserId = plan.value.UserId,
                Content = normalizedContent,
                ResourceIds = plan.value.ResourceIds.ToArray(),
                MediaType = plan.value.MediaType,
                MediaUrl = null,
                LikesCount = plan.value.LikeUsernames.Count,
                CommentsCount = plan.value.Comments.Count,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                IsDeleted = false,
                DeletedAt = null
            };

            posts.Add(post);

            foreach (var hashtagName in hashtagNames)
            {
                if (!hashtags.TryGetValue(hashtagName, out var hashtag))
                {
                    hashtag = new Hashtag
                    {
                        Id = CreateDeterministicGuid($"feed-seed:hashtag:{hashtagName}"),
                        Name = hashtagName,
                        PostCount = 0,
                        CreatedAt = createdAt
                    };

                    hashtags[hashtagName] = hashtag;
                }

                hashtag.PostCount += 1;
                postHashtags.Add(new PostHashtag
                {
                    Id = CreateDeterministicGuid($"feed-seed:post-hashtag:{postId}:{hashtag.Id}"),
                    PostId = postId,
                    HashtagId = hashtag.Id,
                    CreatedAt = createdAt
                });
            }

            for (var commentIndex = 0; commentIndex < plan.value.Comments.Count; commentIndex++)
            {
                var commentPlan = plan.value.Comments[commentIndex];
                var commentCreatedAt = createdAt.AddMinutes(commentIndex + 1);
                var commentId = CreateDeterministicGuid($"feed-seed:comment:{plan.value.Slug}:{commentIndex}:{commentPlan.AuthorUsername}");
                var comment = new Comment
                {
                    Id = commentId,
                    PostId = postId,
                    UserId = usersByUsername[commentPlan.AuthorUsername].Id,
                    ParentCommentId = null,
                    Content = commentPlan.Content,
                    LikesCount = commentPlan.LikesCount,
                    RepliesCount = 0,
                    CreatedAt = commentCreatedAt,
                    UpdatedAt = commentCreatedAt,
                    IsDeleted = false,
                    DeletedAt = null
                };

                comments.Add(comment);
                feedStateComments.Add(new FeedSeedCommentState
                {
                    Id = comment.Id,
                    PostId = postId,
                    AuthorUsername = commentPlan.AuthorUsername,
                    Content = commentPlan.Content
                });
            }

            foreach (var likeUsername in plan.value.LikeUsernames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                postLikes.Add(new PostLike
                {
                    Id = CreateDeterministicGuid($"feed-seed:post-like:{postId}:{likeUsername}"),
                    PostId = postId,
                    UserId = usersByUsername[likeUsername].Id,
                    CreatedAt = createdAt.AddMinutes(2)
                });
            }

            feedStatePosts.Add(new FeedSeedPostState
            {
                Id = postId,
                Slug = plan.value.Slug,
                Username = plan.value.Username,
                ContentPreview = BuildPreview(normalizedContent, 80),
                ResourceCount = plan.value.ResourceIds.Count,
                MediaType = plan.value.MediaType,
                Hashtags = hashtagNames.ToList()
            });
        }

        _dbContext.Follows.AddRange(follows);
        _dbContext.Posts.AddRange(posts);
        _dbContext.Hashtags.AddRange(hashtags.Values);
        _dbContext.PostHashtags.AddRange(postHashtags);
        _dbContext.Comments.AddRange(comments);
        _dbContext.PostLikes.AddRange(postLikes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var state = new FeedSeedState
        {
            SeededAtUtc = now,
            FollowCount = follows.Count,
            PostCount = posts.Count,
            CommentCount = comments.Count,
            LikeCount = postLikes.Count,
            Posts = feedStatePosts,
            Comments = feedStateComments
        };

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(feedStatePath, json, cancellationToken);

        _logger.LogInformation(
            "Seeded feed demo data with {FollowCount} follows, {PostCount} posts, {CommentCount} comments, and {LikeCount} likes.",
            follows.Count,
            posts.Count,
            comments.Count,
            postLikes.Count);
    }

    private async Task<bool> HasExistingFeedDataAsync(CancellationToken cancellationToken)
    {
        var hasPosts = await _dbContext.Posts.AsNoTracking().AnyAsync(post => !post.IsDeleted, cancellationToken);
        if (hasPosts)
        {
            return true;
        }

        if (await _dbContext.Follows.AsNoTracking().AnyAsync(cancellationToken))
        {
            return true;
        }

        if (await _dbContext.Comments.AsNoTracking().AnyAsync(comment => !comment.IsDeleted, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.PostLikes.AsNoTracking().AnyAsync(cancellationToken))
        {
            return true;
        }

        if (await _dbContext.CommentLikes.AsNoTracking().AnyAsync(cancellationToken))
        {
            return true;
        }

        if (await _dbContext.Hashtags.AsNoTracking().AnyAsync(cancellationToken))
        {
            return true;
        }

        if (await _dbContext.PostHashtags.AsNoTracking().AnyAsync(cancellationToken))
        {
            return true;
        }

        return await _dbContext.Reports.AsNoTracking().AnyAsync(cancellationToken);
    }

    private static List<Follow> BuildFollowGraph(IReadOnlyDictionary<string, FeedSeedUserState> usersByUsername)
    {
        var edges = new (string Follower, string Followee)[]
        {
            ("maya_canvas", "leo_travelnotes"),
            ("maya_canvas", "sora_frames"),
            ("maya_canvas", "iris_motion"),
            ("maya_canvas", "nora_bookclub"),
            ("maya_canvas", "linh_overflow_test"),
            ("maya_canvas", "kai_newline"),
            ("maya_canvas", "yuki_firstday"),
            ("leo_travelnotes", "maya_canvas"),
            ("leo_travelnotes", "quang_nomad"),
            ("leo_travelnotes", "vera_grid"),
            ("leo_travelnotes", "zane_looplab"),
            ("leo_travelnotes", "mina_unicode"),
            ("leo_travelnotes", "bao_capsule"),
            ("sora_frames", "maya_canvas"),
            ("sora_frames", "leo_travelnotes"),
            ("sora_frames", "iris_motion"),
            ("sora_frames", "vera_grid"),
            ("sora_frames", "zane_looplab"),
            ("iris_motion", "maya_canvas"),
            ("iris_motion", "sora_frames"),
            ("iris_motion", "leo_travelnotes"),
            ("iris_motion", "quang_nomad"),
            ("iris_motion", "bao_capsule"),
            ("nora_bookclub", "maya_canvas"),
            ("nora_bookclub", "quang_nomad"),
            ("nora_bookclub", "otto_smalltalk"),
            ("nora_bookclub", "pax_reader"),
            ("quang_nomad", "maya_canvas"),
            ("quang_nomad", "leo_travelnotes"),
            ("quang_nomad", "nora_bookclub"),
            ("quang_nomad", "vera_grid"),
            ("vera_grid", "maya_canvas"),
            ("vera_grid", "sora_frames"),
            ("vera_grid", "zane_looplab"),
            ("vera_grid", "iris_motion"),
            ("vera_grid", "linh_overflow_test"),
            ("zane_looplab", "vera_grid"),
            ("zane_looplab", "sora_frames"),
            ("zane_looplab", "maya_canvas"),
            ("zane_looplab", "kai_newline"),
            ("linh_overflow_test", "maya_canvas"),
            ("linh_overflow_test", "leo_travelnotes"),
            ("linh_overflow_test", "mina_unicode"),
            ("linh_overflow_test", "kai_newline"),
            ("otto_smalltalk", "nora_bookclub"),
            ("otto_smalltalk", "maya_canvas"),
            ("mina_unicode", "maya_canvas"),
            ("mina_unicode", "leo_travelnotes"),
            ("mina_unicode", "bao_capsule"),
            ("kai_newline", "maya_canvas"),
            ("kai_newline", "linh_overflow_test"),
            ("hana_numbers", "maya_canvas"),
            ("hana_numbers", "leo_travelnotes"),
            ("hana_numbers", "bao_capsule"),
            ("bao_capsule", "maya_canvas"),
            ("bao_capsule", "hana_numbers"),
            ("bao_capsule", "mina_unicode"),
            ("ria_quietmode", "maya_canvas"),
            ("ria_quietmode", "otto_smalltalk"),
            ("tuan_minimal", "maya_canvas"),
            ("yuki_firstday", "maya_canvas"),
            ("yuki_firstday", "leo_travelnotes"),
            ("yuki_firstday", "kai_newline"),
            ("pax_reader", "nora_bookclub"),
            ("pax_reader", "maya_canvas")
        };

        return edges
            .Distinct()
            .Select((edge, index) => new Follow
            {
                Id = CreateDeterministicGuid($"feed-seed:follow:{edge.Follower}:{edge.Followee}"),
                FollowerId = usersByUsername[edge.Follower].Id,
                FolloweeId = usersByUsername[edge.Followee].Id,
                CreatedAt = DateTime.UtcNow.AddMinutes(-(edges.Length - index + 1))
            })
            .ToList();
    }

    private static List<PostPlan> BuildPostPlans(
        IReadOnlyDictionary<string, FeedSeedUserState> usersByUsername,
        IReadOnlyDictionary<Guid, List<FeedSeedResourceState>> resourcesByUserId)
    {
        var plans = new List<PostPlan>();

        IReadOnlyList<Guid> Media(string username, params string[] fileNames)
        {
            if (!usersByUsername.TryGetValue(username, out var user))
            {
                return Array.Empty<Guid>();
            }

            if (!resourcesByUserId.TryGetValue(user.Id, out var resources))
            {
                return Array.Empty<Guid>();
            }

            var byFile = resources.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
            return fileNames
                .Where(fileName => byFile.ContainsKey(fileName))
                .Select(fileName => byFile[fileName].Id)
                .ToList();
        }

        plans.Add(new PostPlan(
            "maya-short-hello",
            "maya_canvas",
            usersByUsername["maya_canvas"].Id,
            "Chào buổi sáng. Một bài thật ngắn để test card siêu gọn.",
            Array.Empty<Guid>(),
            null,
            new[] { "leo_travelnotes", "sora_frames", "yuki_firstday" },
            new[]
            {
                new CommentPlan("leo_travelnotes", "Nhìn gọn mà sáng quá."),
                new CommentPlan("yuki_firstday", "Em đang test feed đây ạ.")
            }));

        plans.Add(new PostPlan(
            "maya-long-overflow",
            "maya_canvas",
            usersByUsername["maya_canvas"].Id,
            BuildLongOverflowContent(),
            Array.Empty<Guid>(),
            null,
            new[] { "linh_overflow_test", "kai_newline", "mina_unicode", "bao_capsule" },
            new[]
            {
                new CommentPlan("linh_overflow_test", "Case này rất phù hợp để test ellipsis và line clamp."),
                new CommentPlan("otto_smalltalk", "Desktop ổn, giờ xem mobile thế nào."),
                new CommentPlan("pax_reader", "Có cả hashtag lẫn đoạn văn dài, quá đủ để QA." )
            }));

        plans.Add(new PostPlan(
            "maya-multi-image",
            "maya_canvas",
            usersByUsername["maya_canvas"].Id,
            "Một carousel 4 tấm: landscape lớn, gif, square gif và ảnh phụ để test gallery grid #gallery #frontend #imagestress",
            Media("maya_canvas", "landscape.jpg", "landscape2.jpg", "squaregif.gif", "squaregif2.gif"),
            "image",
            new[] { "leo_travelnotes", "vera_grid", "zane_looplab", "hana_numbers", "yuki_firstday" },
            new[]
            {
                new CommentPlan("vera_grid", "Bố cục 4 media kiểu này rất dễ lộ bug spacing."),
                new CommentPlan("zane_looplab", "Gif và jpg đi cùng nhau là case mình đang cần." )
            }));

        plans.Add(new PostPlan(
            "maya-media-only",
            "maya_canvas",
            usersByUsername["maya_canvas"].Id,
            null,
            Media("maya_canvas", "landscape2.jpg"),
            "image",
            new[] { "sora_frames", "iris_motion", "bao_capsule" },
            new[]
            {
                new CommentPlan("sora_frames", "Media-only vẫn phải render đẹp." )
            }));

        plans.Add(new PostPlan(
            "leo-travel-story",
            "leo_travelnotes",
            usersByUsername["leo_travelnotes"].Id,
            "Hôm nay mình đi ngang một con đường rất rộng, gió mạnh, nắng gắt, và vẫn cố chụp lại đủ chi tiết để xem feed có giữ được tỷ lệ khung hay không. #travel #landscape #meai",
            Media("leo_travelnotes", "landscape.jpg"),
            "image",
            new[] { "maya_canvas", "quang_nomad", "yuki_firstday", "mina_unicode" },
            new[]
            {
                new CommentPlan("quang_nomad", "Nhìn đúng mood du mục luôn."),
                new CommentPlan("maya_canvas", "Ảnh ngang rất hợp để test cover crop." )
            }));

        plans.Add(new PostPlan(
            "leo-video-only",
            "leo_travelnotes",
            usersByUsername["leo_travelnotes"].Id,
            null,
            Media("leo_travelnotes", "landscapevideo.mp4"),
            "video",
            new[] { "maya_canvas", "iris_motion", "vera_grid", "tuan_minimal" },
            new[]
            {
                new CommentPlan("iris_motion", "Video ngang dung lượng lớn sẽ giúp test loading state." )
            }));

        plans.Add(new PostPlan(
            "leo-mixed-media",
            "leo_travelnotes",
            usersByUsername["leo_travelnotes"].Id,
            "Mixed media: 1 ảnh + 1 video để frontend xử lý cả thumbnail tĩnh lẫn player trong cùng một card. #mixed #video #image",
            Media("leo_travelnotes", "landscape.jpg", "portailvideo.mp4"),
            "mixed",
            new[] { "maya_canvas", "vera_grid", "zane_looplab", "linh_overflow_test" },
            new[]
            {
                new CommentPlan("vera_grid", "Case mixed này quan trọng cho layout responsive."),
                new CommentPlan("linh_overflow_test", "Hy vọng không vỡ chiều cao item trong masonry." )
            }));

        plans.Add(new PostPlan(
            "sora-grid-stack",
            "sora_frames",
            usersByUsername["sora_frames"].Id,
            "Bài này dùng toàn media vuông + gif để xem feed có chuyển bố cục khi ảnh động xuất hiện không. #grid #gif #ux",
            Media("sora_frames", "squaregif.gif", "squaregif2.gif", "landscape2.jpg"),
            "image",
            new[] { "maya_canvas", "vera_grid", "zane_looplab", "bao_capsule", "hana_numbers" },
            new[]
            {
                new CommentPlan("zane_looplab", "Có animation là biết ngay placeholder có giật hay không." )
            }));

        plans.Add(new PostPlan(
            "iris-portrait-video",
            "iris_motion",
            usersByUsername["iris_motion"].Id,
            "Video dọc để test tỷ lệ 9:16 bên mobile. #portrait #video #reelstyle",
            Media("iris_motion", "portailvideo.mp4"),
            "video",
            new[] { "maya_canvas", "sora_frames", "leo_travelnotes", "yuki_firstday" },
            new[]
            {
                new CommentPlan("maya_canvas", "Case này mà player stretch là thấy liền." )
            }));

        plans.Add(new PostPlan(
            "nora-bookclub-text",
            "nora_bookclub",
            usersByUsername["nora_bookclub"].Id,
            "Một đoạn review vừa đủ dài, có xuống dòng.\n\nMình đang cố mô phỏng post kể chuyện có nhịp nghỉ để frontend test line breaks tự nhiên. #books #review #newline",
            Array.Empty<Guid>(),
            null,
            new[] { "otto_smalltalk", "pax_reader", "maya_canvas" },
            new[]
            {
                new CommentPlan("pax_reader", "Đúng kiểu nội dung mà mình hay lưu để đọc sau."),
                new CommentPlan("otto_smalltalk", "Xuống dòng thế này nhìn dễ thở hơn." )
            }));

        plans.Add(new PostPlan(
            "quang-nomad-url",
            "quang_nomad",
            usersByUsername["quang_nomad"].Id,
            "Link giả lập để test auto-wrap: https://example.com/really/long/path/that/keeps/going/and/going?query=feed-seed&mode=overflow #urltest #nomad",
            Array.Empty<Guid>(),
            null,
            new[] { "leo_travelnotes", "maya_canvas", "hana_numbers" },
            new[]
            {
                new CommentPlan("hana_numbers", "URL dài thường là nơi layout bị đẩy ngang." )
            }));

        plans.Add(new PostPlan(
            "vera-design-system",
            "vera_grid",
            usersByUsername["vera_grid"].Id,
            "Bài nhiều hashtag để test tag wrap #designsystem #spacing #card #responsive #frontend #overflow #media #grid #feed #meai",
            Media("vera_grid", "landscape2.jpg", "squaregif.gif"),
            "image",
            new[] { "maya_canvas", "zane_looplab", "linh_overflow_test", "kai_newline" },
            new[]
            {
                new CommentPlan("kai_newline", "Hashtag wrap mà gãy dòng sai là UI nhìn rất xấu." )
            }));

        plans.Add(new PostPlan(
            "zane-looplab-minimal",
            "zane_looplab",
            usersByUsername["zane_looplab"].Id,
            "ok",
            Array.Empty<Guid>(),
            null,
            new[] { "vera_grid", "maya_canvas" },
            new[]
            {
                new CommentPlan("vera_grid", "Bài siêu ngắn để cân với các bài siêu dài." )
            }));

        plans.Add(new PostPlan(
            "linh-overflow-nospace",
            "linh_overflow_test",
            usersByUsername["linh_overflow_test"].Id,
            "superlongtokenwithoutanybreakwhatsoever_superlongtokenwithoutanybreakwhatsoever_superlongtokenwithoutanybreakwhatsoever #overflow #stress",
            Array.Empty<Guid>(),
            null,
            new[] { "maya_canvas", "kai_newline", "mina_unicode" },
            new[]
            {
                new CommentPlan("kai_newline", "Nếu card không break-word thì sẽ lòi bug ngay." )
            }));

        plans.Add(new PostPlan(
            "mina-unicode-cjk",
            "mina_unicode",
            usersByUsername["mina_unicode"].Id,
            "Unicode check: tiếng Việt có dấu, 日本語の文章, 한국어 테스트, và một ít ký tự đặc biệt như § ¶ • để bảo đảm font fallback hoạt động. #unicode #cjk #frontend",
            Array.Empty<Guid>(),
            null,
            new[] { "maya_canvas", "bao_capsule", "leo_travelnotes" },
            new[]
            {
                new CommentPlan("bao_capsule", "Đa ngôn ngữ giúp test line-height rất tốt." )
            }));

        plans.Add(new PostPlan(
            "kai-newline-poem",
            "kai_newline",
            usersByUsername["kai_newline"].Id,
            "Dòng một.\nDòng hai dài hơn một chút để căn lề.\nDòng ba thì có emoji nhưng mình tạm chưa dùng emoji trong seed này.\nDòng bốn là kết thúc. #multiline",
            Array.Empty<Guid>(),
            null,
            new[] { "maya_canvas", "linh_overflow_test", "yuki_firstday" },
            new[]
            {
                new CommentPlan("yuki_firstday", "Nhìn như một bài thơ mini vậy." )
            }));

        plans.Add(new PostPlan(
            "hana-numbers-grid",
            "hana_numbers",
            usersByUsername["hana_numbers"].Id,
            "123 456 789 101112 131415 161718 192021 để test monospace-like spacing trong feed. #numbers #spacing",
            Array.Empty<Guid>(),
            null,
            new[] { "bao_capsule", "maya_canvas" },
            Array.Empty<CommentPlan>()));

        plans.Add(new PostPlan(
            "bao-capsule-combo",
            "bao_capsule",
            usersByUsername["bao_capsule"].Id,
            "Kết hợp ảnh tĩnh, gif và video trong một post dài vừa phải để test thumbnail ordering. #combo #frontend #media",
            Media("bao_capsule", "landscape2.jpg", "squaregif2.gif", "portailvideo.mp4"),
            "mixed",
            new[] { "maya_canvas", "mina_unicode", "hana_numbers", "leo_travelnotes" },
            new[]
            {
                new CommentPlan("mina_unicode", "Thumbnail order rất hay bị lệch với mixed media."),
                new CommentPlan("leo_travelnotes", "Bài này sẽ khá hữu ích để test skeleton loading." )
            }));

        plans.Add(new PostPlan(
            "ria-quiet-single",
            "ria_quietmode",
            usersByUsername["ria_quietmode"].Id,
            "Mình ít đăng bài, đây là bài gần như duy nhất để test empty-ish profile.",
            Array.Empty<Guid>(),
            null,
            new[] { "maya_canvas" },
            Array.Empty<CommentPlan>()));

        plans.Add(new PostPlan(
            "tuan-minimal-emptycaption",
            "tuan_minimal",
            usersByUsername["tuan_minimal"].Id,
            " ",
            Media("tuan_minimal", "landscape.jpg"),
            "image",
            new[] { "maya_canvas", "leo_travelnotes" },
            new[]
            {
                new CommentPlan("maya_canvas", "Caption trắng phải được normalize thành null." )
            }));

        plans.Add(new PostPlan(
            "yuki-firstday-intro",
            "yuki_firstday",
            usersByUsername["yuki_firstday"].Id,
            "Xin chào mọi người, đây là bài đầu tiên của mình trên feed demo. #firstpost #welcome",
            Array.Empty<Guid>(),
            null,
            new[] { "maya_canvas", "leo_travelnotes", "kai_newline", "otto_smalltalk" },
            new[]
            {
                new CommentPlan("otto_smalltalk", "Welcome aboard."),
                new CommentPlan("maya_canvas", "Bài mở đầu rất hợp để test empty-state chuyển sang populated-state." )
            }));

        plans.Add(new PostPlan(
            "pax-reader-bookmark",
            "pax_reader",
            usersByUsername["pax_reader"].Id,
            "Mình không đăng nhiều, chủ yếu follow để đọc. Nhưng vẫn cần một bài để test profile có đúng một post. #lurker #reader",
            Array.Empty<Guid>(),
            null,
            new[] { "nora_bookclub", "maya_canvas" },
            Array.Empty<CommentPlan>()));

        return plans;
    }

    private static string BuildLongOverflowContent()
    {
        var paragraph = "Đây là một đoạn nội dung dài được cố tình lặp lại để frontend có thể kiểm tra line clamp, read more, spacing giữa các đoạn, và khả năng giữ nhịp typography khi chữ kéo dài qua rất nhiều hàng.";
        return string.Join(
            " ",
            Enumerable.Repeat(paragraph, 12)) +
               " #overflow #longread #frontend #stress #feed #demo";
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> ExtractHashtags(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        return HashtagRegex().Matches(content)
            .Select(match => match.Value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildPreview(string? value, int maxLength = 120)
    {
        var normalized = NormalizeOptionalText(value);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }

    private static string ResolveDataRoot(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath("/seed-data/feed");
        }

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static async Task<bool> WaitForUserStateAsync(string statePath, CancellationToken cancellationToken)
    {
        if (HasStateFileContent(statePath))
        {
            return true;
        }

        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < UserStateWaitTimeout)
        {
            await Task.Delay(UserStatePollInterval, cancellationToken);
            if (HasStateFileContent(statePath))
            {
                return true;
            }
        }

        return HasStateFileContent(statePath);
    }

    private static bool HasStateFileContent(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return false;
        }

        var fileInfo = new FileInfo(statePath);
        return fileInfo.Exists && fileInfo.Length > 0;
    }

    private static async Task<FeedSeedUserStateFile?> LoadUserStateAsync(string statePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<FeedSeedUserStateFile>(stream, SerializerOptions, cancellationToken);
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }

    [GeneratedRegex(@"(?<!\w)#[\p{L}\p{M}\p{N}_]+", RegexOptions.Compiled)]
    private static partial Regex HashtagRegex();

    public sealed class FeedSeedUserStateFile
    {
        public DateTime SeededAtUtc { get; set; }

        public List<FeedSeedUserState> Users { get; set; } = [];

        public List<FeedSeedResourceState> Resources { get; set; } = [];
    }

    public sealed class FeedSeedUserState
    {
        public Guid Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string ProfileKind { get; set; } = string.Empty;

        public bool HasMedia { get; set; }
    }

    public sealed class FeedSeedResourceState
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public string ResourceType { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public string Link { get; set; } = string.Empty;
    }

    private sealed record PostPlan(
        string Slug,
        string Username,
        Guid UserId,
        string? Content,
        IReadOnlyList<Guid> ResourceIds,
        string? MediaType,
        IReadOnlyList<string> LikeUsernames,
        IReadOnlyList<CommentPlan> Comments);

    private sealed record CommentPlan(
        string AuthorUsername,
        string Content,
        int LikesCount = 0);

    public sealed class FeedSeedState
    {
        public DateTime SeededAtUtc { get; set; }

        public int FollowCount { get; set; }

        public int PostCount { get; set; }

        public int CommentCount { get; set; }

        public int LikeCount { get; set; }

        public List<FeedSeedPostState> Posts { get; set; } = [];

        public List<FeedSeedCommentState> Comments { get; set; } = [];
    }

    public sealed class FeedSeedPostState
    {
        public Guid Id { get; set; }

        public string Slug { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string ContentPreview { get; set; } = string.Empty;

        public int ResourceCount { get; set; }

        public string? MediaType { get; set; }

        public List<string> Hashtags { get; set; } = [];
    }

    public sealed class FeedSeedCommentState
    {
        public Guid Id { get; set; }

        public Guid PostId { get; set; }

        public string AuthorUsername { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }
}
