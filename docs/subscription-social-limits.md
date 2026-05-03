# Subscription Social Limits

## Mục tiêu nghiệp vụ

Feature này bổ sung khả năng hiển thị entitlement social account theo subscription hiện tại của user mà không thay đổi rule enforcement đang tồn tại ở các OAuth/link flow. Mục tiêu là để frontend và admin có thể đọc được giới hạn `max/current/remaining social accounts` một cách rõ ràng, đồng thời giữ nguyên contract của các API subscription cũ.

## Endpoint entitlement mới

- `GET /api/User/subscriptions/current/entitlements`
- Auth: user phải đăng nhập
- Unauthorized contract giữ nguyên: `401` với body `MessageResponse("Unauthorized")`
- Success contract:

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

## Cách tính max/current/remaining social accounts

- `maxSocialAccounts`
  - lấy từ `SubscriptionLimits.NumberOfSocialAccounts` nếu user có active subscription + active plan
  - fallback sang free-tier default nếu user chưa có active subscription
- `currentSocialAccounts`
  - đếm số `SocialMedia.Type` distinct của chính user
  - chỉ tính record `!IsDeleted`
- `remainingSocialAccounts`
  - tính bằng `max(0, maxSocialAccounts - currentSocialAccounts)`
- `maxPagesPerSocialAccount`
  - lấy từ entitlement hiện tại để FE biết page cap cho Facebook
- `currentWorkspaceCount`
  - đếm `Workspace` của chính user với `!IsDeleted`
- `maxWorkspaces`
  - lấy từ entitlement hiện tại hoặc fallback free-tier hiện hành

## Free-tier defaults và paid-plan behavior

- Free tier hiện tiếp tục dùng default sẵn có:
  - `maxSocialAccounts = 2`
  - `maxPagesPerSocialAccount = 5`
  - `maxWorkspaces = int.MaxValue`
- Paid plan dùng dữ liệu từ `Subscription.Limits`
- Nếu plan cấu hình `maxSocialAccounts = 0` thì user không được link thêm social account nào

## Error cases khi user vượt limit

Flow link hiện tại không đổi error code:

- Nếu plan không cho phép social linking:
  - `SocialMedia.LimitUnavailable`
- Nếu user đã đạt giới hạn social account:
  - `SocialMedia.LimitExceeded`
- Nếu Facebook page count vượt `MaxPagesPerSocialAccount`:
  - `SocialMedia.PageLimitExceeded`

Enforcement tiếp tục chạy ở:
- các `Initiate*OAuth` command
- `CompleteFacebookOAuthCommand` để chặn race giữa bước initiate và complete

## Backward compatibility cho FE

- Không thay đổi contract của:
  - `GET /api/User/subscriptions/current`
  - `GET /api/User/admin/subscriptions`
  - các error response hiện có của OAuth/link flow
- FE nên gọi endpoint entitlement mới thay vì suy luận social limit từ response plan/subscription cũ
- Response mới chỉ expose user-facing entitlement cần thiết, không trả admin-only fields
