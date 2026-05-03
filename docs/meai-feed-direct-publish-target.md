# MeAI Feed Direct Publish Target

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của direct publish từ `Ai.Microservice` sang MeAI Feed.

### API đã triển khai

- [x] `POST /api/Ai/posts/publish`
- [x] gRPC `FeedPostPublishService.PublishAiPostToFeed`

### Contract hiện tại

- [x] Request publish vẫn hỗ trợ các shape hiện có.
- [x] Field mới `publishToMeAiFeed` là additive và optional.
- [x] Response destination có thêm field nullable `internalTargetKey`.
- [x] Direct publish sang Feed là synchronous.

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

## Luồng publish hiện tại

1. Ai normalize target request.
2. Nếu `publishToMeAiFeed = true`, Ai gọi Feed trước.
3. Nếu Feed fail, toàn bộ request fail.
4. Nếu Feed thành công, Ai thêm destination result cho `meai_feed`.
5. Sau đó Ai tiếp tục flow publish external social media như cũ.

## Chống recursion

Feed direct publish đi qua `CreatePostCommand` với `SkipAiMirror = true`.
Điều này tránh vòng `Ai -> Feed -> Ai` khi Feed bình thường mirror post ngược về Ai.

## Duplicate policy

- Một Ai post chỉ được publish sang MeAI Feed một lần trong v1.
- Feed kiểm tra duplicate qua `posts.ai_post_id`.
- Nếu đã có post cùng `source_ai_post_id`, hệ thống trả lỗi `Post.AlreadyPublishedToFeed`.

## Ghi chú tương thích

- Request cũ không có `publishToMeAiFeed` vẫn hoạt động.
- `internalTargetKey` là field thêm mới, nullable.
- Contract publish external social media hiện có không bị đổi.
