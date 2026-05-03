# AI Usage History For User And Admin

## Endpoints

### User
- `GET /api/Ai/usage/history`
- Requires authenticated user.
- User scope is always forced to the current authenticated user, regardless of any client intent.

### Admin
- `GET /api/Ai/admin/spending/ai/history`
- Requires existing admin authorization on the admin AI spending controller.
- Supports the same filters as the user endpoint plus `userId`.

## Response Contract

Both endpoints return the existing success envelope shape:

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

## Usage Item Fields

Each usage item is sourced from `AiSpendRecord`, with optional timing enrichment:
- `spendRecordId`: `AiSpendRecord.Id`
- `userId`: `AiSpendRecord.UserId`
- `workspaceId`: `AiSpendRecord.WorkspaceId`
- `provider`: AI provider persisted in spend record
- `actionType`: usage action type such as image/video/caption generation
- `model`: model name from spend record
- `variant`: optional model variant
- `unit`: charging unit
- `quantity`: usage quantity
- `unitCostCoins`: per-unit coin cost
- `totalCoins`: total coin cost for the record
- `status`: persisted spend status
- `referenceType`: persisted spend reference type
- `referenceId`: persisted spend reference id
- `createdAt`: spend record creation timestamp
- `updatedAt`: spend record update timestamp
- `startedAtUtc`: optional resolved task start time
- `completedAtUtc`: optional resolved task completion time
- `processingDurationSeconds`: optional computed duration in seconds

## Filters

Supported query parameters for both endpoints:
- `fromUtc` inclusive
- `toUtc` exclusive
- `actionType`
- `status` (case-insensitive match)
- `workspaceId`
- `provider`
- `model`
- `referenceType`
- `cursorCreatedAt`
- `cursorId`
- `limit`

Admin-only query parameter:
- `userId`

Validation rules:
- `fromUtc` must be earlier than `toUtc`
- `limit` defaults to `20`
- `limit` must be greater than `0`
- `limit` must be less than or equal to `100`
- `cursorCreatedAt` requires `cursorId`
- `cursorId` requires `cursorCreatedAt`
- GUID/date/integer query values must parse successfully

Validation and business failures continue to flow through the existing `HandleFailure(result)` contract.

## Cursor Pagination

History uses descending order by:
1. `CreatedAt`
2. `Id` as stable tie-breaker

Cursor pagination uses the pair:
- `cursorCreatedAt`
- `cursorId`

The response returns:
- `nextCursorCreatedAt`
- `nextCursorId`

Clients should pass both returned values together to request the next page.

## Authorization Rules

- User endpoint requires authentication and always resolves history for the authenticated user only.
- Admin endpoint stays under the existing admin controller and keeps its current admin authorization requirement.
- User endpoint preserves the current unauthorized response shape already used by the controller.

## Overview vs History

- `GET /api/Ai/admin/spending/ai` remains the aggregate overview endpoint.
- History endpoints return row-level usage detail for browsing, filtering, and pagination.
- This feature does not replace or change the existing overview contract.

## Data Source Notes

- Primary source of truth is `AiSpendRecord`.
- No schema migration is required for this feature.
- Timing fields are enriched from related image/video task correlation data when available; otherwise those timing fields remain `null`.
