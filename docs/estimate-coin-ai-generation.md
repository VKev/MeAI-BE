# Estimate Coin cho AI Generation

## Trạng thái triển khai

Tài liệu này mô tả backend hiện tại của Estimate Coin cho `Ai.Microservice`, tập trung vào nhóm API `AiGeneration`.

### API đã triển khai

- [x] `POST /api/AiGeneration/estimate`
- [x] `POST /api/Ai/coin-pricing/estimate`
- [x] `GET /api/Ai/coin-pricing`
- [x] `POST /api/Ai/coin-pricing` cho admin
- [x] `PUT /api/Ai/coin-pricing/{id}` cho admin
- [x] `DELETE /api/Ai/coin-pricing/{id}` cho admin

### Flow đã được estimate

- [x] `operation = "captions"`: estimate cho `POST /api/AiGeneration/captions`
- [x] `operation = "post"`: estimate cho `POST /api/AiGeneration/post`
- [x] `operation = "post-prepare"`: estimate `0` coin cho `POST /api/AiGeneration/post-prepare`

## Mục tiêu

Estimate Coin cho phép FE biết trước:

- user sẽ bị trừ bao nhiêu coin nếu bấm generate;
- balance hiện tại có đủ không;
- nếu thiếu thì thiếu bao nhiêu coin để mở top-up modal trước khi gọi generation thật.

Backend không tạo pricing logic mới cho từng màn hình. Tất cả giá vẫn đi qua `CoinPricingCatalogEntry` và `ICoinPricingService`.

## Public estimate theo nghiệp vụ

Endpoint:

```http
POST /api/AiGeneration/estimate
Authorization: Bearer <token>
Content-Type: application/json
```

Request:

```json
{
  "operation": "captions"
}
```

`operation` hỗ trợ:

| Operation | Route thật | Cách tính |
|---|---|---|
| `captions` | `POST /api/AiGeneration/captions` | `caption_generation`, model `openai/gpt-4o`, quantity `1` |
| `post` | `POST /api/AiGeneration/post` | `caption_generation`, model từ active `UserAiConfig.ChatModel`, fallback `gpt-4o-mini`, quantity `1` |
| `post-prepare` | `POST /api/AiGeneration/post-prepare` | `0` coin vì chỉ tạo draft/post builder, không gọi LLM |

Aliases được chấp nhận:

- `caption`, `captions`
- `post`, `gemini-post`, `draft-post`
- `post-prepare`, `prepare-post`, `prepare-posts`

Response thành công:

```json
{
  "isSuccess": true,
  "value": {
    "operation": "captions",
    "actionType": "caption_generation",
    "model": "openai/gpt-4o",
    "variant": null,
    "unit": "per_platform",
    "unitCostCoins": 3,
    "quantity": 1,
    "totalCoins": 3,
    "currentBalance": 10,
    "canAfford": true,
    "shortfallCoins": 0
  },
  "error": {
    "code": "",
    "description": "",
    "metadata": null
  }
}
```

Khi thiếu coin, estimate vẫn trả `200 OK` với:

```json
{
  "canAfford": false,
  "shortfallCoins": 2
}
```

FE nên dùng `canAfford = false` để mở top-up modal trước khi gọi endpoint generate thật.

## Generic pricing estimate

Endpoint generic vẫn tồn tại cho FE/admin hoặc tool nội bộ cần quote trực tiếp theo pricing catalog:

```http
POST /api/Ai/coin-pricing/estimate
Authorization: Bearer <token>
Content-Type: application/json
```

Request:

```json
{
  "actionType": "caption_generation",
  "model": "openai/gpt-4o",
  "variant": null,
  "quantity": 1
}
```

Response là `CoinCostQuote`, không kèm balance:

```json
{
  "actionType": "caption_generation",
  "model": "openai/gpt-4o",
  "variant": null,
  "unit": "per_platform",
  "unitCostCoins": 3,
  "quantity": 1,
  "totalCoins": 3
}
```

## Pricing source of truth

Giá coin nằm trong bảng `coin_pricing_catalog` của `Ai.Microservice`.

Entity:

- `ActionType`
- `Model`
- `Variant`
- `Unit`
- `UnitCostCoins`
- `IsActive`

Resolve order trong `CoinPricingRepository`:

1. match chính xác `actionType + model + variant`;
2. fallback theo model với `variant = null`;
3. fallback theo action với `model = "*"`.

Seeder hiện tạo giá mặc định trong `CoinPricingSeeder`, nhưng runtime catalog trong database là source of truth. Admin có thể chỉnh giá mà không cần redeploy.

## Charge thật khi generate

Estimate không trừ coin. Coin chỉ bị trừ trong các command generate thật:

- `GenerateSocialMediaCaptionsCommand`
- `CreateGeminiPostCommand`
- các flow image/video/chat/recommendation khác dùng cùng `ICoinPricingService` + `IBillingClient`

Luồng debit chuẩn:

1. Resolve giá bằng `ICoinPricingService.GetCostAsync(...)`.
2. Gọi `IBillingClient.DebitAsync(...)` sang `User.Microservice`.
3. `User.Microservice` lock row user bằng PostgreSQL `FOR UPDATE`.
4. Nếu đủ coin, ghi `CoinTransaction` với delta âm.
5. `Ai.Microservice` ghi `AiSpendRecord`.
6. Nếu provider generation fail sau khi debit, backend gọi refund và mark spend record `refunded`.

## Error contract

### Estimate endpoint

Lỗi request sai trả `400 ProblemDetails`.

Ví dụ operation không hỗ trợ:

```json
{
  "status": 400,
  "type": "AiGenerationEstimate.UnsupportedOperation",
  "detail": "operation must be 'captions', 'post', or 'post-prepare'."
}
```

### Generate endpoint

Khi generate thật mà thiếu coin:

- `POST /api/AiGeneration/captions`
- `POST /api/AiGeneration/post`

trả:

```http
402 Payment Required
```

Body:

```json
{
  "status": 402,
  "type": "Billing.InsufficientFunds",
  "detail": "Insufficient MeAI coins."
}
```

FE nên xử lý `402` như tín hiệu mở top-up modal. Các lỗi business khác vẫn đi qua `400 ProblemDetails`.

## FE integration đề xuất

Trước khi user bấm generate:

1. FE gọi `POST /api/AiGeneration/estimate`.
2. Nếu `canAfford = true`, enable nút generate và hiển thị `totalCoins`.
3. Nếu `canAfford = false`, hiển thị thiếu `shortfallCoins` và CTA top-up.
4. Khi user vẫn gọi generate và backend trả `402`, FE mở top-up modal theo lỗi thật từ server.

Ví dụ:

```json
{
  "operation": "post"
}
```

Response thiếu coin:

```json
{
  "operation": "post",
  "totalCoins": 3,
  "currentBalance": 1,
  "canAfford": false,
  "shortfallCoins": 2
}
```

## Lưu ý vận hành

- Không hardcode giá ở FE.
- FE chỉ dùng `operation` với endpoint `AiGeneration/estimate`.
- Giá hiển thị phải lấy từ response estimate hoặc `GET /api/Ai/coin-pricing`.
- Khi provider đổi giá, cập nhật `coin_pricing_catalog` qua admin API hoặc seed/migration phù hợp.
- `post-prepare` luôn là 0 coin cho tới khi backend thêm LLM/provider call vào flow đó.

## Test coverage

Test đã bổ sung:

- estimate `captions` trả quote + balance;
- estimate `post` dùng active `UserAiConfig.ChatModel`;
- estimate `post-prepare` trả 0 coin và không gọi pricing catalog;
- `/api/AiGeneration/captions` map thiếu coin thành `402`;
- `/api/AiGeneration/post` map thiếu coin thành `402`.
