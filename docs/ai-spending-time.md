# AI spending time

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của timing enrichment cho lịch sử usage AI trong `Ai.Microservice`.

### API đã được mở rộng

- [x] `GET /api/Ai/usage/history`
- [x] `GET /api/Ai/usage/summary`
- [x] `GET /api/Ai/admin/spending/ai/history`

### Field timing đã thêm vào response

- [x] `startedAtUtc`
- [x] `completedAtUtc`
- [x] `processingDurationSeconds`

`GET /api/Ai/usage/summary` không trả 3 field timing này. Endpoint đó là aggregate spending theo kỳ thời gian, còn timing enrichment chỉ nằm trên history items.

### Phạm vi timing hiện tại

- [x] Image generation có timing enrichment.
- [x] Video generation có timing enrichment.
- [x] Async draft-post generation có timing enrichment.
- [x] Async improve-post generation có timing enrichment.
- [x] Sync caption/formula/Gemini draft/enhancement endpoints trả `null` cho cả 3 field timing.

## Cách resolve timing

### Image generation

Với record có `referenceType = "chat_image"`:

1. Parse `referenceId` thành `Chat.Id`.
2. Batch load `Chat`.
3. Đọc `Chat.Config` để lấy `correlationId`.
4. Batch load `ImageTask` theo correlation id.
5. Map:
   - `startedAtUtc = ImageTask.CreatedAt`
   - `completedAtUtc = ImageTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` khi `CompletedAt` tồn tại

### Video generation

Với record có `referenceType = "chat_video"`:

1. Parse `referenceId` thành `Chat.Id`.
2. Batch load `Chat`.
3. Đọc `Chat.Config` để lấy `correlationId`.
4. Batch load `VideoTask` theo correlation id.
5. Map:
   - `startedAtUtc = VideoTask.CreatedAt`
   - `completedAtUtc = VideoTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` khi `CompletedAt` tồn tại

### Draft post generation

Với record có `referenceType = "draft_post_generation"`:

1. Parse `referenceId` thành `DraftPostTask.Id`.
2. Batch load `DraftPostTask` theo id.
3. Map:
   - `startedAtUtc = DraftPostTask.CreatedAt`
   - `completedAtUtc = DraftPostTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` khi `CompletedAt` tồn tại

### Improve post generation

Với record có `referenceType = "improve_post"`:

1. Parse `referenceId` thành `RecommendPost.Id`.
2. Batch load `RecommendPost` theo id.
3. Map:
   - `startedAtUtc = RecommendPost.CreatedAt`
   - `completedAtUtc = RecommendPost.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` khi `CompletedAt` tồn tại

## Pending status

Timing enrichment không phụ thuộc trực tiếp vào `AiSpendRecord.Status`, nhưng status giúp frontend hiển thị tiến trình:

- `pending`: record đã được tạo khi request/task được submit; `completedAtUtc` thường là `null`.
- `debited`: task/call hoàn tất thành công; với async flow, `completedAtUtc` thường có giá trị.
- `refunded`: task/call thất bại sau debit và refund đã được áp dụng; với async flow, `completedAtUtc` là thời điểm fail task được lưu.

## API summary spending

### Endpoint

- `GET /api/Ai/usage/summary`
- Auth: user đang đăng nhập.
- Response dùng envelope `Result<AiUsageSummaryResponse>`.

### Query params

| Param | Ý nghĩa |
|---|---|
| `period` | `today`, `week`, hoặc `month`. Mặc định là `month`. |
| `fromUtc` | Bắt đầu custom range, parse theo UTC. |
| `toUtc` | Kết thúc custom range, parse theo UTC. |

Nếu `fromUtc` và `toUtc` cùng có giá trị, backend bỏ qua `period` và trả `period = "custom"`. `fromUtc` phải nhỏ hơn `toUtc`; nếu không endpoint trả `400` với error `AiUsageSummary.InvalidDateRange`.

### Response shape

```json
{
  "isSuccess": true,
  "value": {
    "period": "month",
    "fromUtc": "2026-05-01T00:00:00Z",
    "toUtc": "2026-05-21T12:00:00Z",
    "generatedAtUtc": "2026-05-21T12:00:00Z",
    "totals": {
      "grossCoins": 120.0,
      "refundedCoins": 20.0,
      "netCoins": 100.0,
      "totalRequests": 8
    },
    "spendByAction": [
      {
        "key": "image_generation",
        "label": "Image generation",
        "quantity": 3,
        "grossCoins": 30.0,
        "refundedCoins": 10.0,
        "netCoins": 20.0
      }
    ],
    "spendByModel": [
      {
        "key": "nano-banana-pro",
        "label": "nano-banana-pro",
        "quantity": 3,
        "grossCoins": 30.0,
        "refundedCoins": 10.0,
        "netCoins": 20.0
      }
    ]
  }
}
```

### Cách tính

- Source: `AiSpendRecord` trong range `[fromUtc, toUtc)`, sau đó filter theo `UserId` hiện tại.
- `grossCoins = sum(TotalCoins)` của tất cả record trong range, bao gồm cả `pending`.
- `refundedCoins = sum(TotalCoins)` của record có `status = "refunded"`.
- `netCoins = grossCoins - refundedCoins`.
- `totalRequests = số record`, không phải số task cha; ví dụ image source + reframe variants có thể là nhiều record.
- `quantity` trong breakdown là tổng `Quantity` của từng group.

### Breakdown action order

`spendByAction` luôn trả các action chuẩn theo thứ tự:

1. `image_generation`
2. `image_reframe_variant`
3. `video_generation`
4. `caption_generation`
5. `post_enhancement`
6. `draft_post_generation`
7. `formula_generation`

Các action khác nếu có sẽ được append theo key alphabetically. `spendByModel` group theo `Model` và sort alphabetically.

## Quy tắc trả `null`

Ba field timing được trả `null` khi:

- reference type không hỗ trợ timing enrichment
- `referenceId` không parse được thành id tương ứng (`Chat.Id`, `DraftPostTask.Id`, hoặc `RecommendPost.Id`)
- chat không tồn tại với `chat_image` / `chat_video`
- `Chat.Config` không hợp lệ hoặc không đọc được correlation id với `chat_image` / `chat_video`
- task image/video không tìm thấy
- draft/improve task không tìm thấy

## Hạn chế hiện tại

- Timing chỉ là read-side join, không được lưu ngược vào `AiSpendRecord`.
- Nếu config chat thay đổi sau này, timing join phụ thuộc vào dữ liệu hiện tại đang lưu.
- Sync-only operations chưa có task entity riêng để resolve duration nên vẫn trả `null`.

## Hiệu năng

Enrichment được xử lý theo batch theo page dữ liệu để tránh N+1 query: chat image/video gom theo chat/correlation id, draft/improve gom theo task id.
