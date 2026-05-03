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

Chỉ package active mới xuất hiện trong public catalog.

## Checkout

`POST /api/User/billing/coin-packages/{packageId}/checkout`

Luồng hiện tại:

1. Validate package tồn tại, active, giá hợp lệ, và currency `usd`.
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
- Idempotency được đảm bảo theo transaction.
