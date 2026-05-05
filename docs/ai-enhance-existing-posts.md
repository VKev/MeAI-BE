# AI Enhance Existing Posts

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của feature enhance post trong `Ai.Microservice`.

### API đã triển khai

- [x] `POST /api/Ai/posts/{postId}/enhance`

### Contract hiện tại

- [x] Request có các field `platform`, `resourceIds`, `language`, `instruction`, `suggestionCount`.
- [x] `platform` bắt buộc.
- [x] `resourceIds` là tùy chọn.
- [x] `suggestionCount` mặc định `3`.
- [x] `suggestionCount` bị chặn tối đa `6`.
- [x] Response vẫn dùng envelope `Result<T>`.
- [x] Response trả `bestSuggestion` và `alternatives`.

### Giới hạn hiện tại

- [x] `bestSuggestion` là suggestion đầu tiên của model output.
- [x] `alternatives` chứa các suggestion còn lại.
- [x] V1 chỉ làm suggestion, không tự ghi đè post đã lưu.
- [x] Resource context có thể fallback sang nội dung của post hiện có khi request không truyền `resourceIds`.

## Mục tiêu

Feature này cho phép người dùng yêu cầu AI cải thiện một post hiện có theo từng nền tảng.

## Request/Response

Request:

```json
{
  "platform": "instagram",
  "resourceIds": ["uuid"],
  "language": "vi",
  "instruction": "giọng thân thiện, ngắn gọn",
  "suggestionCount": 3
}
```

Response:

```json
{
  "isSuccess": true,
  "value": {
    "postId": "uuid",
    "platform": "ig",
    "resourceIds": ["uuid"],
    "bestSuggestion": {
      "caption": "...",
      "hashtags": ["#meai"],
      "trendingHashtags": ["#marketing"],
      "callToAction": "..."
    },
    "alternatives": [
      {
        "caption": "...",
        "hashtags": [],
        "trendingHashtags": [],
        "callToAction": "..."
      }
    ]
  }
}
```

## Resource context

Thứ tự resolve resource context:

1. `resourceIds` từ request nếu có.
2. `post.Content.ResourceList` của post hiện có nếu request không truyền `resourceIds`.
3. Text-only enhancement nếu không có resource nào để dùng.

## Billing

- Billing dùng action type `post_enhancement`.
- Coin được debit trước khi gọi AI.
- Nếu AI fail sau debit thì hệ thống refund toàn bộ đúng một lần.

## Validation và lỗi

- `Post.NotFound`
- `Post.Unauthorized`
- `SocialMedia.InvalidType`
- `Billing.InsufficientFunds`

## Frontend behavior

FE nên dùng endpoint này như một generator suggestion, sau đó áp suggestion đã chọn vào flow update post hiện có.
