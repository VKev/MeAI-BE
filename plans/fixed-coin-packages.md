# Fixed Coin Packages

## Implementation Status

- [x] DONE - Add fixed `CoinPackage` entity and persistence mapping
- [x] DONE - Add `coin_packages` database migration and snapshot updates
- [x] DONE - Expose public coin package catalog for frontend
- [x] DONE - Return only active packages in public listing
- [x] DONE - Add Stripe one-time checkout creation for coin packages
- [x] DONE - Create pending `Transaction` audit records for checkout
- [x] DONE - Persist Stripe `PaymentIntent` reference on checkout transaction
- [x] DONE - Add resolve-checkout API for coin package payments
- [x] DONE - Route webhook and resolve through the same confirm handler
- [x] DONE - Keep confirm handler as the only coin-credit write path
- [x] DONE - Credit `User.MeAiCoin` from successful package payment
- [x] DONE - Append `CoinTransaction` ledger entries for successful credits
- [x] DONE - Enforce idempotent crediting per `Transaction.Id`
- [x] DONE - Detect webhook/resolve replays and noop safely
- [x] DONE - Add admin list/create/update/delete package APIs
- [x] DONE - Keep package management independent from subscription catalog
- [x] DONE - Preserve payment audit contract in `Transaction`
- [x] DONE - Preserve balance ledger contract in `CoinTransaction`
- [x] DONE - Add targeted tests for listing, checkout, resolve, idempotency, and admin flows
- [x] DONE - Create `docs/fixed-coin-packages.md`

## Summary

Feature này triển khai mua coin theo gói cố định, không hỗ trợ số coin tùy ý trong v1.

Mục tiêu:
- User mua gói coin qua Stripe one-time payment.
- Thanh toán và audit nằm ở `Transaction`.
- Biến động số dư coin nằm ở `CoinTransaction`.
- Credit coin phải idempotent.

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- `docs/fixed-coin-packages.md`

File docs này phải chốt:
- catalog gói coin và field public cho FE
- flow checkout/resolve/webhook
- mapping giữa `Transaction` và `CoinTransaction`
- idempotency rules khi credit coin
- admin APIs quản lý package
- khác biệt giữa mua coin và mua subscription

## Current State

Code đã có sẵn:
- `User.Microservice/src/Domain/Entities/User.cs` với `MeAiCoin`
- `User.Microservice/src/Domain/Entities/CoinTransaction.cs`
- `User.Microservice/src/Domain/Entities/Transaction.cs`
- `User.Microservice/src/Infrastructure/Logic/Services/BillingService.cs`
- `User.Microservice/src/WebApi/Controllers/BillingCardsController.cs`
- `User.Microservice/src/WebApi/Controllers/StripeWebhooksController.cs`
- subscription checkout flow tại `PurchaseSubscriptionCommand`

Hiện trạng xác nhận:
- Hệ thống đã có card setup và Stripe payment plumbing.
- Hệ thống chưa có catalog coin package.
- Hệ thống chưa có one-time top-up flow cho coin.

## Data Model

Thêm entity mới `CoinPackage` trong `User.Microservice`:

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | user-facing |
| `CoinAmount` | `decimal(18,2)` | số coin base |
| `BonusCoins` | `decimal(18,2)` | coin thưởng |
| `Price` | `decimal(18,2)` | giá thanh toán |
| `Currency` | `string` | v1 mặc định `usd` |
| `IsActive` | `bool` | soft-active |
| `DisplayOrder` | `int` | FE sorting |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime?` | |

Không đổi schema `CoinTransaction`.

`Transaction` tiếp tục là payment audit source of truth:
- `RelationType = "CoinPackage"`
- `RelationId = CoinPackage.Id`
- `PaymentMethod = "Stripe"`
- `TransactionType = "coin_package_purchase"`
- `ProviderReferenceId = Stripe PaymentIntentId`

`CoinTransaction` là balance ledger source of truth:
- `Delta = CoinAmount + BonusCoins`
- `Reason = "coin_package.purchase"`
- `ReferenceType = "coin_package"`
- `ReferenceId = Transaction.Id`

## Public API

User APIs:
- `GET /api/User/billing/coin-packages`
- `POST /api/User/billing/coin-packages/{packageId}/checkout`
- `POST /api/User/billing/coin-packages/resolve-checkout`

Admin APIs:
- `GET /api/User/admin/billing/coin-packages`
- `POST /api/User/admin/billing/coin-packages`
- `PUT /api/User/admin/billing/coin-packages/{packageId}`
- `DELETE /api/User/admin/billing/coin-packages/{packageId}`

`GET /api/User/billing/coin-packages` response:

```json
{
  "isSuccess": true,
  "value": [
    {
      "id": "uuid",
      "name": "500 Coins",
      "coinAmount": 500,
      "bonusCoins": 50,
      "totalCoins": 550,
      "price": 4.99,
      "currency": "usd",
      "displayOrder": 1
    }
  ]
}
```

`POST /checkout` response:

```json
{
  "isSuccess": true,
  "value": {
    "packageId": "uuid",
    "transactionId": "uuid",
    "paymentIntentId": "pi_xxx",
    "clientSecret": "secret",
    "status": "requires_payment_method",
    "amountDue": 4.99,
    "currency": "usd"
  }
}
```

## Implementation Changes

Thêm Stripe service method mới:
- `CreateCoinPackagePaymentIntentAsync(...)`
- `GetCoinPackageCheckoutStatusAsync(...)`

Checkout flow:
1. Validate package active.
2. Tạo `Transaction` status pending.
3. Tạo Stripe PaymentIntent one-time.
4. Lưu `ProviderReferenceId = paymentIntentId`.
5. Trả `clientSecret` cho FE.

Resolve flow:
1. Nhận `paymentIntentId`.
2. Query Stripe status.
3. Nếu thành công:
   - mark `Transaction.Status = succeeded`
   - credit `User.MeAiCoin`
   - append `CoinTransaction`
4. Nếu retry hoặc webhook chạy lại:
   - detect theo `Transaction.ProviderReferenceId`
   - nếu đã credit rồi thì noop thành công

Webhook:
- `StripeWebhooksController` phải gọi cùng một confirm handler với resolve endpoint.
- Confirm handler là nơi duy nhất được phép credit coin.

## Validation And Errors

Error cases:
- package không tồn tại hoặc inactive
- package price không hợp lệ
- payment intent không match transaction/package
- Stripe status chưa thành công

Idempotency:
- credit coin duy nhất một lần trên mỗi `Transaction.Id`
- `CoinTransaction.ReferenceId = Transaction.Id` là khóa dedupe nghiệp vụ

## Tests

Happy path:
- list chỉ trả package active.
- checkout tạo transaction pending + payment intent.
- resolve payment success credit đúng `CoinAmount + BonusCoins`.

Admin:
- create/update/deactivate package hoạt động độc lập với subscription catalog.

Idempotency:
- gọi resolve 2 lần không double-credit.
- webhook và resolve cùng chạy cũng không double-credit.

Ledger consistency:
- `Transaction` phản ánh trạng thái thanh toán.
- `CoinTransaction` phản ánh biến động số dư.

## Assumptions

- V1 chỉ hỗ trợ `usd`.
- FE hiện tại đã có Stripe Elements/card setup nên top-up sẽ reuse flow thanh toán đó.
- Không có discount code hay dynamic promotion trong scope này.
