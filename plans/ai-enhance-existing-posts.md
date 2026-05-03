# AI Enhance Existing Posts

## Execution Checklist

- [x] DONE Read the plan and inspect existing AI post enhancement, billing, controller, and test code paths.
- [x] DONE Implement the enhance existing post application flow and API contract.
- [x] DONE Add billing action type support and refund behavior for post enhancement.
- [x] DONE Add or update automated tests covering happy path, billing, security, and output shape.
- [x] DONE Create `docs/ai-enhance-existing-posts.md` and capture the requested feature details.
- [x] DONE Run targeted validation and verify all requested plan work is complete.

## Summary

Feature này cho phép AI tăng cường các post hiện có. Scope v1 chỉ làm text enhancement:
- caption
- hashtags
- call to action

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- `docs/ai-enhance-existing-posts.md`

File docs này phải mô tả:
- endpoint enhance mới
- input/output contract
- cách chọn resource context
- billing/refund behavior
- các capability nằm ngoài scope v1
- hướng FE áp dụng suggestion vào post hiện có

Không nằm trong scope v1:
- re-generate media
- reframe image
- suggest schedule
- multi-step optimization suite

## Current State

Code đã có sẵn:
- `Ai.Microservice/src/Application/Posts/Commands/GenerateSocialMediaCaptionsCommand.cs`
- `Ai.Microservice/src/Application/Abstractions/Gemini/IGeminiCaptionService.cs`
- `Ai.Microservice/src/Domain/Repositories/IPostRepository.cs`
- `Ai.Microservice/src/WebApi/Controllers/PostsController.cs`
- `Ai.Microservice/src/WebApi/Controllers/AiGenerationController.cs`

Hiện trạng xác nhận:
- Hệ thống đã có caption generation theo social platform.
- Handler hiện tại generate caption theo batch social media items.
- Chưa có route riêng cho “enhance post hiện có”.

## Public API

Thêm endpoint:
- `POST /api/Ai/posts/{postId}/enhance`

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
    "platform": "instagram",
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

Quy ước:
- `bestSuggestion` là phần tử đầu tiên từ model output.
- `alternatives` là các phần tử còn lại.
- `resourceIds` nếu request không truyền thì fallback sang resource của post hiện có.

## Implementation Changes

Thêm command:
- `EnhanceExistingPostCommand`

Flow:
1. Load post theo `postId`
2. Check owner và `DeletedAt`
3. Resolve `platform`
4. Resolve `resourceIds`:
   - request override nếu có
   - fallback `post.Content.ResourceList`
5. Gọi `IGeminiCaptionService.GenerateSocialMediaCaptionsAsync`
6. Map result thành `bestSuggestion + alternatives`

Pricing:
- thêm action type mới trong coin catalog: `post_enhancement`
- billing model mặc định giống caption generation hiện tại
- debit trước call, refund toàn bộ nếu AI fail

Không đổi:
- caption batch endpoint hiện tại
- schema `Post`

## Validation And Errors

Validation:
- `platform` bắt buộc
- `suggestionCount` default `3`, max `6`
- post phải thuộc user hiện tại
- nếu cả request `resourceIds` và post resources đều rỗng thì vẫn cho phép enhance text-only

Errors:
- `Post.NotFound`
- `Post.Unauthorized`
- `SocialMedia.InvalidType`
- `Billing.InsufficientFunds`

## Tests

Happy path:
- post text-only vẫn enhance được.
- post có media dùng đúng context media.
- request override `resourceIds` được ưu tiên hơn resource của post.

Billing:
- debit trước khi gọi AI.
- AI fail thì refund đúng 1 lần.

Security:
- user không enhance được post của người khác.

Output shape:
- response luôn có `bestSuggestion`.
- `alternatives` rỗng nếu chỉ xin `1` suggestion.

## Assumptions

- V1 chỉ làm text enhancement và reuse caption generation engine hiện tại.
- FE sẽ áp suggestion vào draft/post bằng flow update hiện có, không cần auto-write vào post trong endpoint này.
