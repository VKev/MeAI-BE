# AI Usage History For User And Admin

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của lịch sử usage AI chi tiết cho user và admin trong `Ai.Microservice`.

### API đã triển khai

- [x] `GET /api/Ai/usage/history`
- [x] `GET /api/Ai/admin/spending/ai/history`

### Nguồn dữ liệu

- [x] Dữ liệu lấy từ `AiSpendRecord`.
- [x] Admin overview aggregate `GET /api/Ai/admin/spending/ai` đọc cùng ledger và có breakdown cho các action AI mới.
- [x] User endpoint chỉ xem record của chính user hiện tại.
- [x] Các flow async ghi record ở trạng thái `pending` ngay khi submit, rồi consumer chuyển sang `debited` hoặc `refunded`.

### Flow đang được ghi history/spending

| Flow | `actionType` | `referenceType` | Provider |
|---|---|---|---|
| Chat image generation | `image_generation` | `chat_image` | `kie` |
| Chat image reframe/variant | `image_reframe_variant` | `chat_image` | `kie` |
| Chat video generation | `video_generation` | `chat_video` | `kie` |
| Social caption batch | `caption_generation` | `caption_batch` | `openrouter` |
| Gemini draft post | `caption_generation` | `gemini_draft_post` | `kie` |
| Existing post enhancement, sync endpoint | `post_enhancement` | `post_enhancement` | `kie` |
| Async improve post | `post_enhancement` | `improve_post` | `openrouter` |
| Async draft post generation | `draft_post_generation` | `draft_post_generation` | `openrouter` |
| Prompt formula generation | `formula_generation` | `formula_generation` | `kie` |

RAG query, rerank, web/image search, and other internal helper calls are tracked under the parent user-facing operation above when they are part of a generation flow; they do not create independent `AiSpendRecord` rows.

## Response contract

Response dùng envelope `Result<T>`.

```json
{
  "isSuccess": true,
  "value": {
    "items": [
      {
        "spendRecordId": "uuid",
        "userId": "uuid",
        "workspaceId": "uuid-or-null",
        "provider": "kie",
        "actionType": "image_generation",
        "model": "nano-banana-pro",
        "variant": "1K",
        "unit": "per_image",
        "quantity": 1,
        "unitCostCoins": 4.0,
        "totalCoins": 4.0,
        "status": "pending",
        "referenceType": "chat_image",
        "referenceId": "uuid",
        "createdAt": "2026-05-03T10:00:00Z",
        "updatedAt": "2026-05-03T10:01:00Z",
        "startedAtUtc": "2026-05-03T10:00:00Z",
        "completedAtUtc": "2026-05-03T10:00:12Z",
        "processingDurationSeconds": 12
      }
    ],
    "nextCursorCreatedAt": "2026-05-03T10:00:00Z",
    "nextCursorId": "uuid"
  }
}
```

## Query và filter

### User

- `fromUtc`
- `toUtc`
- `actionType`
- `status`
- `workspaceId`
- `provider`
- `model`
- `referenceType`
- `cursorCreatedAt`
- `cursorId`
- `limit`

### Admin

Tất cả filter của user, cộng thêm:

- `userId`

## Quy tắc hiện tại

- Sort giảm dần theo `CreatedAt`, tie-break bằng `Id`.
- `fromUtc` inclusive.
- `toUtc` exclusive.
- `limit` mặc định `20`.
- `limit` tối đa `100`.
- `status` match case-insensitive.
- `cursorCreatedAt` và `cursorId` phải đi cùng nhau.

## Status semantics

| Status | Ý nghĩa |
|---|---|
| `pending` | Coin đã được debit/reserve và AI job đang chạy hoặc sync call chưa hoàn tất. |
| `debited` | AI operation hoàn tất thành công, record được tính là spend thực tế. |
| `refunded` | Operation thất bại sau khi debit và refund đã được áp dụng/idempotent. |
| `failed` | Trạng thái dự phòng cho failure không refund được; hiện các flow chính dùng `refunded` khi refund thành công. |

Với các flow sync như caption/formula/enhancement, record có thể chỉ ở `pending` trong thời gian request đang xử lý. Với async image/video/draft/improve, frontend có thể thấy `pending` trong history cho tới khi consumer nhận callback hoặc hoàn tất task.

## Authorization

- User endpoint yêu cầu đăng nhập.
- Admin endpoint tiếp tục dùng authorization admin hiện có.
- Unauthorized response của user endpoint giữ nguyên contract hiện tại.

## Timing enrichment

Khi dữ liệu phù hợp, item history có thể được enrich thêm:

- `startedAtUtc`
- `completedAtUtc`
- `processingDurationSeconds`

Timing hiện hỗ trợ `chat_image`, `chat_video`, `draft_post_generation`, và `improve_post`. Xem chi tiết logic timing trong `docs/ai-spending-time.md`.
