# AI Usage History For User And Admin

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của lịch sử usage AI chi tiết cho user và admin trong `Ai.Microservice`.

### API đã triển khai

- [x] `GET /api/Ai/usage/history`
- [x] `GET /api/Ai/admin/spending/ai/history`

### Nguồn dữ liệu

- [x] Dữ liệu lấy từ `AiSpendRecord`.
- [x] Admin overview aggregate `GET /api/Ai/admin/spending/ai` vẫn giữ nguyên.
- [x] User endpoint chỉ xem record của chính user hiện tại.

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
        "status": "debited",
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

## Authorization

- User endpoint yêu cầu đăng nhập.
- Admin endpoint tiếp tục dùng authorization admin hiện có.
- Unauthorized response của user endpoint giữ nguyên contract hiện tại.

## Timing enrichment

Khi dữ liệu phù hợp, item history có thể được enrich thêm:

- `startedAtUtc`
- `completedAtUtc`
- `processingDurationSeconds`

Xem chi tiết logic timing trong `docs/ai-spending-time.md`.
