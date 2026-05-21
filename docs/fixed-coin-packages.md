# Fixed Coin Packages

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của feature coin package cố định trong `User.Microservice`.

### API đã triển khai

- [x] `GET /api/User/billing/coin-packages`
- [x] `POST /api/User/billing/coin-packages/{packageId}/checkout` (hỗ trợ `useDefaultCard` query parameter)
- [x] `POST /api/User/billing/coin-packages/resolve-checkout`
- [x] `GET /api/User/admin/billing/coin-packages`
- [x] `POST /api/User/admin/billing/coin-packages`
- [x] `PUT /api/User/admin/billing/coin-packages/{packageId}`
- [x] `DELETE /api/User/admin/billing/coin-packages/{packageId}`

### Seed data

- [x] Có startup seeder cho `coin_packages`
- Seeder mặc định tham chiếu 3 tier subscription hiện có để tạo catalog coin package
- Currency của coin package bám theo cùng `Stripe:Currency` config như subscription, hiện mặc định là `vnd`
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

`POST /api/User/billing/coin-packages/{packageId}/checkout?useDefaultCard={bool}`

Hỗ trợ mua coin trực tiếp thông qua thẻ mặc định đã lưu (`useDefaultCard=true`):

1. Validate package tồn tại, đang `active`, giá hợp lệ, và currency khớp `Stripe:Currency`.
2. Tạo hoặc resolve Stripe customer cho user.
3. Nếu `useDefaultCard=true`:
   - Tìm kiếm phương thức thanh toán mặc định (`default_payment_method`) đã lưu trên Stripe của customer.
   - Nếu không có thẻ lưu mặc định, trả về lỗi `Stripe.DefaultPaymentMethodNotFound`.
4. Tạo `Transaction` pending.
5. Tạo Stripe `PaymentIntent`:
   - Nếu `useDefaultCard=true`, kích hoạt off-session payment (`Confirm = true`, `OffSession = true`, gán `PaymentMethod`).
   - Nếu không, tạo PaymentIntent thông thường chờ xác thực từ frontend.
6. Đồng bộ hóa trạng thái thanh toán:
   - Nếu thanh toán thành công ngay lập tức (`succeeded`), gọi trực tiếp lệnh confirm để cộng coin đồng bộ cho user.
   - Nếu thanh toán thất bại hoặc yêu cầu xác thực thêm (như 3D Secure / `authentication_required`), bắt ngoại lệ gracefully để trả về `ClientSecret` cùng trạng thái thanh toán, cho phép frontend mở giao diện xác thực (Stripe Elements/3DS sheet).
7. Trả payload checkout về cho frontend.

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
- Hỗ trợ cơ chế tự động trừ tiền qua thẻ lưu mặc định (`useDefaultCard=true`) thông qua `off_session` và auto-confirm trên Stripe, đi kèm graceful fallback về 3D Secure ở frontend nếu có yêu cầu từ ngân hàng.
- Idempotency được đảm bảo theo transaction.
