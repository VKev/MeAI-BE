# MeAI Feed Direct Publish Target

## Execution Status

- [x] DONE Read and validate the existing Ai/Feed/shared proto publish flow
- [x] DONE Extend `POST /api/Ai/posts/publish` to support `publishToMeAiFeed` without breaking older request shapes
- [x] DONE Add additive MeAI Feed destination response metadata with `internalTargetKey`
- [x] DONE Add dedicated Ai -> Feed gRPC contract in `SharedLibrary/Protos/feed_posts.proto`
- [x] DONE Implement Ai direct publish orchestration for MeAI Feed before external targets
- [x] DONE Implement Feed gRPC publish endpoint for Ai-initiated MeAI Feed posts
- [x] DONE Prevent recursion by skipping Feed -> Ai mirror creation for Ai-originated direct publish
- [x] DONE Prevent duplicate MeAI Feed publish for the same Ai post
- [x] DONE Preserve mixed-target behavior for Feed + external social publish in one request
- [x] DONE Add targeted automated coverage for Ai and Feed direct publish scenarios
- [x] DONE Create `docs/meai-feed-direct-publish-target.md`
- [x] DONE Run targeted validation for Ai and Feed direct publish changes

## Summary

Feature này thêm `MeAI Feed` như một publish target mới trong flow publish hiện tại của Ai service.

Quyết định đã chốt:
- Đây là target tùy chọn mới.
- Không auto mirror mọi social publish.
- Không làm route feed-only riêng ở v1.

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- `docs/meai-feed-direct-publish-target.md`

File docs này phải bao gồm:
- request/response mới của publish flow
- contract nội bộ Ai -> Feed
- quy tắc chống recursion với feed mirror
- hành vi khi publish đồng thời feed + social ngoài
- duplicate policy cho publish lên MeAI Feed
- backward compatibility notes cho FE

## Current State

Code đã có sẵn:
- `Ai.Microservice/src/Application/Posts/Commands/PublishPostsCommand.cs`
- `Ai.Microservice/src/WebApi/Controllers/PostsController.cs`
- `Feed.Microservice/src/Application/Posts/Commands/CreatePostCommand.cs`
- `Feed.Microservice/src/Infrastructure/Logic/Ai/AiFeedPostGrpcService.cs`
- `Ai.Microservice/src/WebApi/Grpc/AiFeedPostGrpcService.cs`
- `SharedLibrary/Protos/ai_feed.proto`

Hiện trạng xác nhận:
- Feed post user tạo tay hiện mirror sang Ai draft qua `CreateMirrorPost`.
- Ai publish flow hiện chỉ hỗ trợ social media IDs ngoài hệ thống.
- Nếu Ai gọi thẳng `Feed CreatePostCommand` hiện tại thì sẽ gây vòng lặp mirror ngược về Ai.

## Public API Changes

Mở rộng request cho `POST /api/Ai/posts/publish`:

```json
{
  "postId": "uuid",
  "socialMediaIds": ["uuid"],
  "isPrivate": false,
  "publishToMeAiFeed": true
}
```

Request cũ vẫn hợp lệ nếu không truyền `publishToMeAiFeed`.

Mở rộng response item additively:

```json
{
  "socialMediaId": "uuid-or-null",
  "socialMediaType": "meai_feed",
  "pageId": "meai_feed",
  "externalPostId": "feed-post-id",
  "publicationId": null,
  "publishStatus": "published",
  "internalTargetKey": "meai_feed"
}
```

Field mới:
- `internalTargetKey` nullable

Quy ước:
- target nội bộ MeAI Feed trả `socialMediaType = "meai_feed"`
- `externalPostId` chứa `Feed.Post.Id`

## Interface Changes

Thêm proto mới `SharedLibrary/Protos/feed_posts.proto` để Ai gọi Feed:

```proto
service FeedPostPublishService {
  rpc PublishAiPostToFeed (PublishAiPostToFeedRequest) returns (PublishAiPostToFeedResponse);
}
```

Request tối thiểu:
- `user_id`
- `workspace_id`
- `source_ai_post_id`
- `content`
- `resource_ids`
- `media_type`

Response:
- `feed_post_id`
- `created_at`

Không reuse `ai_feed.proto` cho chiều gọi này.

## Implementation Changes

### Ai service

Mở rộng `PublishPostTargetInput`:
- thêm `bool PublishToMeAiFeed`

Flow mới trong `PublishPostsCommandHandler`:
1. Load và authorize post như hiện tại.
2. Nếu `PublishToMeAiFeed = true` thì gọi Feed gRPC trước.
3. Nếu Feed publish fail thì fail whole request, chưa tạo placeholder external nào.
4. Nếu Feed publish success thì append destination result nội bộ vào response.
5. Sau đó tiếp tục external publish flow hiện tại cho `socialMediaIds`.

### Feed service

Thêm gRPC server handler mới nhận publish từ Ai.

Feed-side create logic phải có cờ `SkipAiMirror = true` để tránh recursion:
- không gọi `IAiFeedPostService.CreateMirrorPostAsync`
- vẫn tạo `Post`, hashtag, notification như post feed bình thường

Origin dữ liệu:
- resource giữ nguyên `resourceIds` từ Ai post
- author là chính user publish

## Validation And Errors

Rules:
- một target có thể chỉ publish lên MeAI Feed, chỉ publish social ngoài, hoặc cả hai
- nếu cả `socialMediaIds` rỗng và `publishToMeAiFeed = false` thì vẫn trả lỗi hiện tại
- nếu publish lên MeAI Feed nhiều lần cho cùng một Ai post trong v1 thì block bằng error `Post.AlreadyPublishedToFeed`

Idempotency:
- Feed service lưu `source_ai_post_id` ở metadata hoặc mapping table để detect duplicate

## Tests

Happy path:
- publish chỉ lên MeAI Feed thành công.
- publish MeAI Feed + Facebook/Instagram trong cùng request thành công.

Recursion safety:
- Ai publish sang Feed không tạo vòng lặp mirror ngược về Ai.

Failure handling:
- Feed publish fail thì external placeholder chưa được tạo.
- external publish fail sau khi Feed publish thành công vẫn giữ feed post đã tạo.

Security:
- chỉ owner của Ai post mới publish được.

## Assumptions

- V1 coi MeAI Feed là target internal synchronous.
- Feed post được tạo ra là published ngay, không cần async consumer riêng.
