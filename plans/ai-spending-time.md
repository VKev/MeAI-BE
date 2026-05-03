# AI Spending Time

## Execution Status

- [x] DONE Review AI usage history timing requirements and existing code paths
- [x] DONE Extend AI usage history responses with nullable timing fields
- [x] DONE Implement batched chat/task timing resolution for image and video usage records
- [x] DONE Keep caption generation timing unresolved in v1 and return `null` fields
- [x] DONE Add resolver and query tests for happy paths, fallbacks, and batched loading
- [x] DONE Create `docs/ai-spending-time.md`
- [x] DONE Validate with targeted tests and build

## Summary

Update này bổ sung thông tin thời lượng xử lý cho lịch sử AI usage. Đây là một update riêng, nhưng implementation sẽ gắn trực tiếp vào response của history APIs thay vì tạo endpoint mới.

## Docs Deliverable

Sau khi implement xong update này, phải tạo thêm file docs:
- `docs/ai-spending-time.md`

File docs này phải chốt:
- field timing nào được expose
- cách resolve timing cho image/video
- vì sao caption generation chưa có timing ở v1
- quy tắc trả `null`
- performance note cho batch resolution
- known limitations của read-side join

## Current State

Code đã có sẵn:
- `Ai.Microservice/src/Domain/Entities/ImageTask.cs`
- `Ai.Microservice/src/Domain/Entities/VideoTask.cs`
- `Ai.Microservice/src/Domain/Entities/AiSpendRecord.cs`
- `Ai.Microservice/src/Domain/Entities/Chat.cs`
- `ImageTask` và `VideoTask` đều có `CreatedAt` và `CompletedAt`

Hiện trạng xác nhận:
- `AiSpendRecord` chưa lưu duration.
- `ImageTask` và `VideoTask` có dữ liệu đủ để tính duration.
- `AiSpendRecord.ReferenceId` của image/video đang trỏ tới `Chat.Id`, không trỏ trực tiếp tới `CorrelationId`.

## Public API Changes

Mở rộng `AiUsageHistoryItemResponse` thêm các field nullable:

```json
{
  "startedAtUtc": "2026-05-03T10:00:00Z",
  "completedAtUtc": "2026-05-03T10:01:30Z",
  "processingDurationSeconds": 90
}
```

Quy ước:
- `startedAtUtc` ưu tiên lấy từ task row.
- `completedAtUtc` lấy từ task row.
- `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)`.
- Nếu chưa resolve được task hoặc action type không hỗ trợ, cả 3 field phải là `null`.

## Implementation Changes

Không thêm bảng mới ở v1.

Thêm resolver nội bộ:
- `IAiUsageTimingResolver`

Resolution rules:

### Image generation

1. `AiSpendRecord.ReferenceType == "chat_image"`
2. Parse `ReferenceId` thành `Chat.Id`
3. Đọc `Chat.Config`
4. Parse `correlationId`
5. Lookup `ImageTask` theo `CorrelationId`
6. Map `ImageTask.CreatedAt` và `CompletedAt`

### Video generation

1. `AiSpendRecord.ReferenceType == "chat_video"`
2. Parse `ReferenceId` thành `Chat.Id`
3. Đọc `Chat.Config`
4. Parse `correlationId`
5. Lookup `VideoTask` theo `CorrelationId`
6. Map `VideoTask.CreatedAt` và `CompletedAt`

### Caption generation

V1 trả `null` cho cả 3 field vì flow caption chưa có task entity riêng và chưa lưu timing chuẩn.

## Performance Rules

History query không được N+1 từng record.

Handler phải:
1. materialize page `AiSpendRecord`
2. group theo `ReferenceType`
3. batch load `Chat`
4. extract correlation ids
5. batch load `ImageTask` / `VideoTask`
6. map duration trong memory

## Tests

Happy path:
- image spend record có `processingDurationSeconds` đúng.
- video spend record có `processingDurationSeconds` đúng.

Fallbacks:
- task chưa completed thì `completedAtUtc = null`, `processingDurationSeconds = null`.
- chat config hỏng hoặc không parse được correlation id thì duration là `null`.
- caption generation luôn trả `null`.

Performance:
- page 20 records không tạo query vòng lặp theo từng item.

## Assumptions

- `ImageTask.CreatedAt` và `VideoTask.CreatedAt` được xem là mốc bắt đầu xử lý đủ tốt cho v1.
- V1 không backfill duration vào `AiSpendRecord`; chỉ resolve ở read side.
