# MeAI Feed Direct Publish Target

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của direct publish từ `Ai.Microservice` sang MeAI Feed.

### API đã triển khai

- [x] `POST /api/Ai/posts/publish`
- [x] gRPC `FeedPostPublishService.PublishAiPostToFeed`
- [x] gRPC `FeedPostPublishService.UnpublishAiPostFromFeed`
- [x] gRPC `FeedPostPublishService.UpdateAiPostOnFeed`
- [x] gRPC `AiFeedPostService.DeleteMirrorPost`

### Contract hiện tại

- [x] Request publish vẫn hỗ trợ các shape hiện có.
- [x] Field mới `publishToMeAiFeed` là additive và optional.
- [x] Response destination có thêm field nullable `internalTargetKey`.
- [x] Direct publish sang Feed là synchronous.
- [x] Publish sang Feed tạo `PostPublication` type `meai_feed` bên Ai để các flow
  `unpublish` và `update-publish` xử lý được Feed như một target đã publish.
- [x] Xóa post trực tiếp bên Feed sẽ xóa mirror post bên creator/Ai.
- [x] Unpublish từ creator/Ai chỉ xóa bản Feed publication, không xóa ngược source post.

## Request publish

```json
{
  "postId": "uuid",
  "socialMediaIds": ["uuid"],
  "isPrivate": false,
  "publishToMeAiFeed": true
}
```

## Response destination

Ví dụ kết quả cho MeAI Feed:

```json
{
  "socialMediaId": null,
  "socialMediaType": "meai_feed",
  "pageId": "meai_feed",
  "externalPostId": "feed-post-id",
  "publicationId": null,
  "publishStatus": "published",
  "internalTargetKey": "meai_feed"
}
```

## Publication record trong Ai

Khi `publishToMeAiFeed = true` thành công, Ai không chỉ trả destination result mà còn lưu
một `PostPublication` để Feed target tham gia cùng lifecycle với các platform khác.

Giá trị quan trọng:

```text
SocialMediaType = "meai_feed"
SocialMediaId = Guid.Empty
DestinationOwnerId = "meai_feed"
ExternalContentId = <feed_post_id>
ExternalContentIdType = "post_id"
PublishStatus = "published"
```

Row này là source of truth để:

- `POST /api/Ai/posts/{postId}/unpublish` tìm thấy Feed target và gỡ khỏi Feed.
- `PUT /api/Ai/posts/{postId}/publish` / update-publish tìm thấy Feed target và cập nhật
  caption/content bên Feed.
- response post/builders hiển thị Feed như một published destination giống các social target.

## Luồng publish hiện tại

1. Ai normalize target request.
2. Nếu `publishToMeAiFeed = true`, Ai gọi Feed trước.
3. Nếu Feed fail, toàn bộ request fail.
4. Nếu Feed thành công, Ai thêm destination result cho `meai_feed`.
5. Ai lưu `PostPublication` `meai_feed` với `ExternalContentId = feed_post_id`.
6. Sau đó Ai tiếp tục flow publish external social media như cũ.

## Luồng unpublish từ creator/Ai

Khi user gọi unpublish trên creator/Ai:

1. `UnpublishPostCommand` lấy các `PostPublication` còn active (`PublishStatus = "published"`).
2. Nếu publication có `SocialMediaType = "meai_feed"`, `UnpublishFromTargetConsumer` gọi
   `FeedPostPublishService.UnpublishAiPostFromFeed`.
3. Feed soft-delete post tương ứng bằng `DeletePostCommand` với `SkipAiMirrorDelete = true`.
4. Ai soft-delete `PostPublication` `meai_feed`.
5. Batch unpublish finalize như các platform khác; nếu không còn publication active, post quay về `draft`.

Điểm quan trọng: unpublish từ creator/Ai **không xóa source Ai post**. Nó chỉ gỡ bản đã
publish sang Feed.

## Luồng update-publish từ creator/Ai

Khi user cập nhật caption/content của post đã publish:

1. `UpdatePublishedPostCommand` cập nhật `Post.Content` bên Ai.
2. Với publication `SocialMediaType = "meai_feed"`, `UpdatePublishedTargetConsumer` gọi
   `FeedPostPublishService.UpdateAiPostOnFeed`.
3. Feed cập nhật `content` của Feed post tương ứng.
4. Feed giữ nguyên `resourceIds` và `mediaType` hiện có để tránh làm mất media của post.
5. Notification update target/batch vẫn đi theo flow hiện tại.

Giới hạn hiện tại:

- Update-publish sang Feed chỉ sync text/caption.
- Media/resource update không nằm trong contract `UpdateAiPostOnFeed` hiện tại.

## Chống recursion

Feed direct publish đi qua `CreatePostCommand` với `SkipAiMirror = true`.
Điều này tránh vòng `Ai -> Feed -> Ai` khi Feed bình thường mirror post ngược về Ai.

Unpublish từ Ai sang Feed đi qua `DeletePostCommand` với `SkipAiMirrorDelete = true`.
Điều này tránh vòng `Ai unpublish -> Feed delete -> Ai delete source post`.

Ngược lại, khi user xóa post trực tiếp trong Feed API, Feed vẫn gọi
`AiFeedPostService.DeleteMirrorPost` nếu post đó có `AiPostId`. Đây là hành vi cố ý:
xóa từ Feed nghĩa là xóa cả mirror bên creator; unpublish từ creator nghĩa là chỉ gỡ target Feed.

## Duplicate policy

- Một Ai post chỉ được publish sang MeAI Feed một lần trong v1.
- Feed kiểm tra duplicate qua `posts.ai_post_id`.
- Nếu đã có post cùng `source_ai_post_id`, hệ thống trả lỗi `Post.AlreadyPublishedToFeed`.

## gRPC contracts

Shared proto files:

- `Backend/Microservices/SharedLibrary/Protos/feed_posts.proto`
- `Backend/Microservices/SharedLibrary/Protos/ai_feed.proto`

`FeedPostPublishService`:

- `PublishAiPostToFeed`: Ai tạo bản Feed từ source Ai post.
- `UnpublishAiPostFromFeed`: Ai gỡ bản Feed khi creator unpublish target `meai_feed`.
- `UpdateAiPostOnFeed`: Ai cập nhật content bản Feed khi creator update-publish.
- `GetFeedPostForModeration`: Ai lấy content Feed post để moderation/recommendation.

`AiFeedPostService`:

- `CreateMirrorPost`: Feed tạo mirror post bên Ai khi user tạo post trực tiếp trong Feed.
- `DeleteMirrorPost`: Feed xóa mirror post bên Ai khi user xóa post trực tiếp trong Feed.

## Ghi chú tương thích

- Request cũ không có `publishToMeAiFeed` vẫn hoạt động.
- `internalTargetKey` là field thêm mới, nullable.
- Contract publish external social media hiện có không bị đổi.
- Các publication `meai_feed` cũ trước thay đổi này có thể chưa có `PostPublication` row;
  chỉ các lần publish mới có đủ dữ liệu để unpublish/update-publish tự động qua Feed.
