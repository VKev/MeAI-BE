# Fixed Coin Packages

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của feature coin package cố định trong `User.Microservice`.

### API đã triển khai

- [x] `GET /api/User/billing/coin-packages`
- [x] `POST /api/User/billing/coin-packages/{packageId}/checkout`
- [x] `POST /api/User/billing/coin-packages/resolve-checkout`
- [x] `GET /api/User/admin/billing/coin-packages`
- [x] `POST /api/User/admin/billing/coin-packages`
- [x] `PUT /api/User/admin/billing/coin-packages/{packageId}`
- [x] `DELETE /api/User/admin/billing/coin-packages/{packageId}`

### Seed data

- [x] Có startup seeder cho `coin_packages`
- Seeder mặc định tham chiếu 3 tier subscription hiện có để tạo catalog coin package
- Catalog mặc định:
  - `Coin Package 10000` -> `coinAmount = 10000`, `bonusCoins = 0`, `price = 100000`, `currency = vnd`
  - `Coin Package 15000` -> `coinAmount = 15000`, `bonusCoins = 0`, `price = 150000`, `currency = vnd`
  - `Coin Package 20000` -> `coinAmount = 20000`, `bonusCoins = 0`, `price = 200000`, `currency = vnd`

## Mục tiêu

Feature này cho phép user mua coin theo package cố định qua Stripe one-time payment.

## Public catalog

Các field public hiện trả về:

- `id`
- `name`
- `coinAmount`
- `bonusCoins`
- `totalCoins`
- `price`
- `currency`
- `displayOrder`

Public catalog chỉ trả package `active` theo `displayOrder`.
Package `inactive` vẫn còn trong dữ liệu để admin và các màn hình lịch sử nội bộ có thể resolve lại record cũ.

## Checkout

`POST /api/User/billing/coin-packages/{packageId}/checkout`

Luồng hiện tại:

1. Validate package tồn tại, đang `active`, giá hợp lệ, và currency `vnd`.
2. Tạo hoặc resolve Stripe customer cho user.
3. Tạo `Transaction` pending.
4. Tạo Stripe `PaymentIntent`.
5. Lưu `ProviderReferenceId` và status payment vào `Transaction`.
6. Trả payload checkout cho FE.

## Resolve và webhook

`POST /api/User/billing/coin-packages/resolve-checkout` và webhook Stripe dùng cùng confirm command để tránh double credit.

## Mapping dữ liệu

- `Transaction` là payment audit record.
- `CoinTransaction` là balance ledger record.
- Credit coin chỉ thực hiện sau khi Stripe báo trạng thái thành công.

## Admin APIs

Admin có thể:

- xem danh sách package
- tạo package
- cập nhật package
- xóa mềm package bằng cách deactivate

## Lưu ý

- Coin package là luồng one-time payment.
- Không phải subscription recurring.
- PaymentIntent của coin package không gắn `off_session`, nên không mở đường cho auto-renew hoặc recharge nền.
- Idempotency được đảm bảo theo transaction.
