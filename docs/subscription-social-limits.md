# Subscription Social Limits

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của entitlement social account theo subscription trong `User.Microservice`.

### API đã triển khai

- [x] `GET /api/User/subscriptions/current/entitlements`

### Contract hiện tại

- [x] Unauthorized trả `MessageResponse("Unauthorized")`.
- [x] Response là `Result<T>`.
- [x] Không đổi contract của `GET /api/User/subscriptions/current`.

## Entitlement hiện tại

Response hiện có:

```json
{
  "isSuccess": true,
  "value": {
    "hasActivePlan": true,
    "currentSubscriptionId": "uuid-or-null",
    "currentPlanId": "uuid-or-null",
    "currentPlanName": "Pro",
    "maxSocialAccounts": 8,
    "currentSocialAccounts": 3,
    "remainingSocialAccounts": 5,
    "maxPagesPerSocialAccount": 10,
    "currentWorkspaceCount": 2,
    "maxWorkspaces": 5
  }
}
```

## Cách tính

- `maxSocialAccounts`
  - lấy từ `SubscriptionLimits.NumberOfSocialAccounts`
  - nếu user chưa có active subscription thì fallback free tier
- `currentSocialAccounts`
  - đếm social media record đang active của user
- `remainingSocialAccounts`
  - `max(0, maxSocialAccounts - currentSocialAccounts)`
- `maxPagesPerSocialAccount`
  - lấy từ entitlement hiện tại
- `currentWorkspaceCount`
  - đếm workspace đang active của user
- `maxWorkspaces`
  - lấy từ entitlement hiện tại hoặc fallback free tier

## Free tier hiện tại

- `maxSocialAccounts = 2`
- `maxPagesPerSocialAccount = 5`
- `maxWorkspaces = int.MaxValue`

## Enforcement

User bị chặn ở các OAuth/link flow hiện có khi vượt limit:

- `InitiateFacebookOAuthCommand`
- `CompleteFacebookOAuthCommand`
- `InitiateInstagramOAuthCommand`
- `CompleteInstagramOAuthCommand`
- `InitiateTikTokOAuthCommand`
- `CompleteTikTokOAuthCommand`
- `InitiateThreadsOAuthCommand`
- `CompleteThreadsOAuthCommand`

## Admin chỉnh sửa

Admin có thể chỉnh các giá trị limit thông qua subscription admin APIs vì `Subscription.Limits` được map trực tiếp trong:

- `POST /api/User/admin/subscriptions`
- `PUT /api/User/admin/subscriptions/{id}`
- `PATCH /api/User/admin/subscriptions/{id}`

### Field limit liên quan

- `number_of_social_accounts`
- `max_pages_per_social_account`
- `number_of_workspaces`

## Ghi chú

- FE nên gọi endpoint entitlement mới thay vì suy luận limit từ subscription response cũ.
- `MaxPagesPerSocialAccount` chủ yếu dùng cho Facebook page cap.
