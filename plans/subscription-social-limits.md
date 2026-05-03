# Subscription Social Limits

## Summary

Tài liệu này chốt scope cho feature giới hạn số lượng social account theo subscription plan.

Mục tiêu của v1:
- Giữ nguyên enforcement hiện có ở OAuth/link flow.
- Bổ sung read API rõ ràng để FE và admin biết entitlement hiện tại của user.
- Không thay đổi contract của các API subscription hiện có nếu không cần.

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- [x] `docs/subscription-social-limits.md`

File docs này là tài liệu trạng thái triển khai thực tế, không phải plan. Nội dung tối thiểu:
- [x] mục tiêu nghiệp vụ
- [x] endpoint entitlement mới
- [x] cách tính `max/current/remaining social accounts`
- [x] free-tier defaults và paid-plan behavior
- [x] error cases khi user vượt limit
- [x] ghi chú backward compatibility cho FE

## Current State

Code đã có sẵn:
- `User.Microservice/src/Domain/Entities/SubscriptionLimits.cs`
- `User.Microservice/src/Application/Subscriptions/Services/IUserSubscriptionEntitlementService.cs`
- `User.Microservice/src/Application/Subscriptions/Services/UserSubscriptionEntitlementService.cs`
- `User.Microservice/src/Application/SocialMedias/Commands/InitiateFacebookOAuthCommand.cs`
- `User.Microservice/src/Application/SocialMedias/Commands/CompleteFacebookOAuthCommand.cs`
- `User.Microservice/src/Application/SocialMedias/Commands/InitiateInstagramOAuthCommand.cs`
- `User.Microservice/src/Application/SocialMedias/Commands/InitiateTikTokOAuthCommand.cs`
- `User.Microservice/src/Application/SocialMedias/Commands/InitiateThreadsOAuthCommand.cs`

Hiện trạng xác nhận:
- `SubscriptionLimits.NumberOfSocialAccounts` đã tồn tại.
- `SubscriptionLimits.MaxPagesPerSocialAccount` đã tồn tại.
- `EnsureSocialAccountLinkAllowedAsync` đã block khi user vượt limit social account.
- Facebook page selection đã có enforcement riêng cho `MaxPagesPerSocialAccount`.
- Free tier mặc định hiện đang cho phép `2` social accounts.

## Public API Changes

Thêm endpoint mới:

`GET /api/User/subscriptions/current/entitlements`

Response:

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

Quy ước:
- `currentSocialAccounts` chỉ đếm social media record đang active, không deleted.
- `remainingSocialAccounts = max(0, maxSocialAccounts - currentSocialAccounts)`.
- `maxSocialAccounts = 0` nghĩa là plan không cho phép link social account.
- Free tier tiếp tục dùng fallback hiện tại nếu user chưa có active subscription.

Không đổi:
- `GET /api/User/subscriptions/current`
- `GET /api/User/admin/subscriptions`
- contract lỗi hiện tại của các flow OAuth/link

## Implementation Changes

Không thay đổi schema database.

Thêm read model:
- [x] `CurrentSubscriptionEntitlementsResponse`

Thêm query/handler:
- [x] `GetCurrentSubscriptionEntitlementsQuery`
- [x] handler reuse `IUserSubscriptionEntitlementService`

Handler phải tính thêm:
- [x] `currentSocialAccounts` bằng cách query social media active của user
- [x] `currentWorkspaceCount` bằng cách query workspace active của user

Controller:
- [x] thêm action mới vào `SubscriptionsController`
- [x] tiếp tục trả `Unauthorized(new MessageResponse("Unauthorized"))` nếu không có user id

## Validation And Errors

Không thêm error code mới cho flow link hiện tại.

Endpoint entitlement mới:
- [x] Nếu user không tồn tại, trả business failure theo chuẩn service hiện tại.
- [x] Nếu claim không hợp lệ, trả unauthorized như các endpoint user khác.

## Tests

Happy path:
- [x] Free tier user chưa link social nào trả `maxSocialAccounts = 2`, `currentSocialAccounts = 0`.
- [x] Paid plan user trả đúng `maxSocialAccounts` theo `SubscriptionLimits`.

Limit enforcement:
- [x] User đã đạt limit thì `Initiate*OAuth` bị block.
- [x] User đã đạt limit thì `Complete*OAuth` cũng bị block nếu có race giữa initiate và complete.
- [x] Facebook page count vượt `MaxPagesPerSocialAccount` vẫn giữ lỗi hiện tại.

Security:
- [x] User chỉ đọc được entitlement của chính mình.
- [x] Không expose admin-only data trong response mới.

## Assumptions

- Scope v1 không thay đổi rule tính limit, chỉ bổ sung visibility.
- FE sẽ gọi endpoint entitlement mới thay vì suy luận limit từ plan response cũ.
