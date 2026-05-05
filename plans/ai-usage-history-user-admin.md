# AI Usage History For User And Admin

## Summary

Feature này bổ sung lịch sử usage AI chi tiết cho cả user và admin. Scope này không thay thế overview tổng hợp hiện tại, mà bổ sung thêm read API chi tiết.

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- `docs/ai-usage-history-user-admin.md`

File docs này phải mô tả:
- endpoint user/admin mới
- fields trả về của usage item
- filter và cursor pagination
- quy tắc authorization
- khác biệt giữa overview aggregate và history detail
- ghi chú nguồn dữ liệu từ `AiSpendRecord`

## Current State

Code đã có sẵn:
- `Ai.Microservice/src/Domain/Entities/AiSpendRecord.cs`
- `Ai.Microservice/src/Infrastructure/Repositories/AiSpendRecordRepository.cs`
- `Ai.Microservice/src/Application/Admin/Queries/GetAdminAiSpendOverviewQuery.cs`
- `Ai.Microservice/src/WebApi/Controllers/AdminAiSpendingController.cs`
- `User.Microservice/src/Domain/Entities/CoinTransaction.cs`

Hiện trạng xác nhận:
- `AiSpendRecord` đã lưu spend cho image/video/caption.
- Admin hiện chỉ có endpoint aggregate `GET /api/Ai/admin/spending/ai`.
- User chưa có endpoint riêng để xem usage AI detail.
- `CoinTransaction` chỉ là ledger số dư, không đủ làm usage history đầy đủ.

## Public API

User endpoint mới:
- `GET /api/Ai/usage/history`

Admin endpoint mới:
- `GET /api/Ai/admin/spending/ai/history`

Query params hỗ trợ:
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

Admin-only params:
- `userId`

Response chung:

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
        "quantity": 3,
        "unitCostCoins": 4.00,
        "totalCoins": 12.00,
        "status": "debited",
        "referenceType": "chat_image",
        "referenceId": "uuid",
        "createdAt": "2026-05-03T10:00:00Z",
        "updatedAt": "2026-05-03T10:01:00Z"
      }
    ],
    "nextCursorCreatedAt": "2026-05-03T10:00:00Z",
    "nextCursorId": "uuid"
  }
}
```

Quy ước:
- Sort giảm dần theo `CreatedAt`, tie-break bằng `Id`.
- Cursor pagination dùng cặp `createdAt + id`.
- User chỉ thấy record của chính mình.

Không đổi:
- `GET /api/Ai/admin/spending/ai`

## Implementation Changes

Thêm read model:
- `AiUsageHistoryResponse`
- `AiUsageHistoryItemResponse`

Thêm queries:
- `GetMyAiUsageHistoryQuery`
- `GetAdminAiUsageHistoryQuery`

Repository:
- thêm query path filterable trên `AiSpendRecord`
- không cần schema migration

Controller:
- thêm controller user-facing mới hoặc action mới trong `ChatsController`/`AdminAiSpendingController`
- route admin giữ cùng prefix `api/Ai/admin/spending/ai`

Filtering rules:
- `fromUtc` inclusive
- `toUtc` exclusive
- `limit` default `20`, max `100`
- `status` so khớp case-insensitive

## Validation And Errors

Lỗi validation:
- `fromUtc >= toUtc`
- `limit <= 0`
- `limit > 100`
- `cursorCreatedAt` có nhưng `cursorId` không hợp lệ

Error shape:
- tiếp tục đi qua `HandleFailure(result)` như các endpoint Ai hiện tại

## Tests

Happy path:
- user lấy được lịch sử usage của chính mình theo thời gian.
- admin lấy được lịch sử của toàn hệ thống.

Filtering:
- filter theo `actionType`
- filter theo `status`
- filter theo `workspaceId`
- filter theo `userId` ở admin route

Pagination:
- page 1 và page 2 không trùng item.
- stable ordering khi nhiều row có cùng timestamp.

Security:
- user không xem được usage của người khác.
- admin route vẫn yêu cầu quyền admin như hiện tại.

## Execution Status

- [x] DONE Review existing AI spend domain, repository, query, controller, and test structure.
- [x] DONE Add shared AI usage history filter/query contracts and response models.
- [x] DONE Implement user AI usage history MediatR query and controller endpoint.
- [x] DONE Implement admin AI usage history MediatR query and controller endpoint.
- [x] DONE Extend `AiSpendRecord` repository history filtering and stable cursor pagination.
- [x] DONE Preserve existing success and failure response contracts via `Ok(result)` and `HandleFailure(result)`.
- [x] DONE Add focused query, controller, and repository tests for user/admin usage history.
- [x] DONE Create `docs/ai-usage-history-user-admin.md`.
- [x] DONE Validate with targeted AI usage history tests.
- [x] DONE Validate with targeted AI WebApi build.

## Assumptions

- V1 không join thêm username/email vào response.
- Nếu admin UI cần tên user, FE sẽ resolve qua User admin APIs riêng.
