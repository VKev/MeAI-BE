# FR-A5 API Key Management — Requirement, Backend & Frontend Integration

## Mục đích tài liệu hợp nhất

Tài liệu này là một bản duy nhất, dùng chung cho cả backend, frontend, admin UI, runtime credential provider, và checklist nghiệm thu.

Cách đọc nhanh:

- **Phần 1** mô tả requirement nghiệp vụ, thiết kế backend, database, startup sync, runtime provider, API contract, bảo mật và tiêu chí hoàn thành.
- **Phần 2** mô tả cách frontend tích hợp màn hình quản lý API key dựa trên API backend hiện có.
- `env` chỉ là nguồn bootstrap lúc service khởi động.
- `database` là runtime source of truth sau khi sync xong.
- `admin` là nhóm duy nhất được quyền xem, thêm, sửa, bật/tắt và rotate key.
- nghiệp vụ gọi provider bên ngoài phải luôn resolve credential mới nhất từ database/runtime provider.

---

# Phần 1 — Requirement, Backend Design & Runtime Behavior

## Mục tiêu tài liệu

Tài liệu này chốt yêu cầu nghiệp vụ và thiết kế mục tiêu cho `FR-A5` của `admin` về `API key management`.

Phạm vi của `FR-A5` gồm:

- `FR-A5.1`: hệ thống cho phép admin cấu hình, cập nhật, và rotate API key/credential cho social networks, Stripe, và AI providers.
- `FR-A5.2`: chỉ admin được ủy quyền mới có quyền truy cập các API quản lý API key.

Ngoài 2 ý gốc ở trên, requirement nghiệp vụ đã được chốt thêm:

- mỗi microservice phải tự đồng bộ các key/credential hiện có trong `env` của chính service đó lên database khi service khởi động;
- nếu database đã có key trùng tên thì service phải `upsert` và ghi đè bằng giá trị từ `env`;
- admin phải có thể thêm mới một key hoặc chỉnh sửa một key hiện có;
- mọi nghiệp vụ trong hệ thống đang dùng key phải luôn lấy giá trị mới nhất đã được admin chỉnh sửa trong database, không tiếp tục coi `env` là source of truth ở runtime;
- nếu một service có key trong `env` thì service đó phải có endpoint quản lý key riêng cho service đó.

## Bối cảnh repo hiện tại

Repo hiện đã có precedent phù hợp để triển khai `FR-A5`:

- `User.Microservice` đã có admin-only config endpoint tại `Backend/Microservices/User.Microservice/src/WebApi/Controllers/AdminConfigController.cs`.
- Role restriction hiện dùng `SharedLibrary.Attributes.AuthorizeAttribute` tại `Backend/Microservices/SharedLibrary/Attributes/AuthorizeAttribute.cs`.
- `User.Microservice` đang có nhiều credential đọc từ config/env như:
  - `Stripe:PublishableKey`
  - `Stripe:SecretKey`
  - `Stripe:WebhookSecret`
  - `Facebook:AppId`
  - `Facebook:AppSecret`
  - `Instagram:AppId`
  - `Instagram:AppSecret`
  - `TikTok:ClientKey`
  - `TikTok:ClientSecret`
  - `Threads:AppId`
  - `Threads:AppSecret`
- `Ai.Microservice` cũng đang có provider keys đọc từ config/env như:
  - `Gemini:ApiKey`
  - `Kie:ApiKey`
  - `Kie:CallbackUrl`
  - `N8n:InternalCallbackToken`

Điều này cho thấy `FR-A5` không phải tạo ra một nhu cầu hoàn toàn mới; nó là bước chuẩn hóa để các secret/runtime credentials không còn bị phân tán chỉ trong `env` và constructor config binding nữa.

## Vấn đề hiện tại cần giải quyết

Hiện trạng phổ biến trong repo là service đọc key trực tiếp từ `IConfiguration` hoặc `IOptions` rồi giữ trong object runtime. Cách này có 3 vấn đề:

1. Admin không có màn hình/API tập trung để xem và cập nhật key.
2. Giá trị trong `env` có thể khác với giá trị admin muốn dùng sau khi hệ thống đã chạy.
3. Một số service giữ key trong memory từ lúc startup, nên nếu admin sửa key sau đó thì nghiệp vụ vẫn có thể tiếp tục dùng key cũ.

`FR-A5` phải giải quyết đúng 3 điểm này.

## Yêu cầu nghiệp vụ đã chốt

### 1. Đồng bộ `env` lên database khi service khởi động

Mỗi service phải tự quét danh sách key/credential mà service đó sở hữu hoặc sử dụng trực tiếp.

Ví dụ:

- `User.Microservice` chịu trách nhiệm sync các key của Facebook, Instagram, TikTok, Threads, Stripe, Email, S3 nếu service đó thực sự dùng các giá trị đó.
- `Ai.Microservice` chịu trách nhiệm sync các key của Gemini, Kie, n8n callback token, và các AI/provider credential khác mà service đó dùng trực tiếp.

Hành vi bắt buộc:

- khi service startup, hệ thống đọc các giá trị hiện có từ `env`/configuration;
- với mỗi key có mặt trong `env`, service phải ghi vào bảng API key management trong database;
- nếu đã tồn tại bản ghi cùng `ServiceName + KeyName` thì phải cập nhật và ghi đè;
- nếu chưa tồn tại thì tạo mới;
- nếu key không có trong `env` thì không tự động xóa bản ghi đang có trong database.

Rule này biến `env` thành bootstrap source, còn database là runtime source of truth sau khi sync xong.

### 2. Admin có thể xem, thêm, và chỉnh sửa key

Admin phải có thể:

- xem danh sách key thuộc từng service;
- xem metadata của key;
- thêm một key mới nếu key đó chưa tồn tại;
- chỉnh sửa giá trị hoặc metadata của key hiện có;
- rotate key bằng cách cập nhật giá trị mới;
- bật/tắt key nếu cần rollout an toàn;
- theo dõi ai đã chỉnh sửa và chỉnh sửa lúc nào.

Admin không bắt buộc phải xem raw secret ở dạng đầy đủ trong mọi response. Mặc định nên chỉ trả về masked value, và chỉ các flow cập nhật/ghi mới mới nhận plaintext input.

### 3. Chỉ admin được truy cập

Mọi API quản lý key phải là `admin-only`.

Rule authorization:

- chỉ principal có role `ADMIN` hoặc `Admin` mới được gọi;
- user thường, kể cả đã authenticated, không được xem danh sách key hay chỉnh sửa key;
- unauthorized phải giữ contract hiện có của hệ thống;
- forbidden phải trả về đúng semantic "insufficient permissions".

Điểm này phù hợp với pattern đang có tại `AdminConfigController` và `AuthorizeAttribute`.

### 4. Mỗi service có endpoint quản lý key riêng

Không gom toàn bộ write API vào một service trung tâm duy nhất.

Nếu một service có key trong `env`, service đó phải tự expose endpoint admin để quản lý phần key của chính nó.

Endpoint hiện có:

- `User.Microservice`
  - `GET /api/User/admin/api-keys`
  - `POST /api/User/admin/api-keys`
  - `PUT /api/User/admin/api-keys/{id}`
- `Ai.Microservice`
  - `GET /api/Ai/admin/api-keys`
  - `POST /api/Ai/admin/api-keys`
  - `PUT /api/Ai/admin/api-keys/{id}`

Service nào không có secret/key riêng thì không cần endpoint này. Route `/rotate` riêng chưa tồn tại; rotate hiện là `PUT {id}` với `value` mới.

### 5. Mọi nghiệp vụ phải luôn dùng key mới nhất

Đây là rule kỹ thuật quan trọng nhất của `FR-A5`.

Sau khi `FR-A5` được áp dụng:

- code nghiệp vụ không được tiếp tục coi `IConfiguration["X:ApiKey"]` là runtime source of truth;
- code nghiệp vụ không được cache vô thời hạn raw key đọc từ `env` ở constructor nếu key đó có thể bị admin cập nhật;
- trước khi gọi provider bên ngoài, service phải resolve key mới nhất từ database hoặc từ một runtime provider có cơ chế reload từ database;
- nếu admin vừa cập nhật key, các request phát sinh sau đó phải dùng key mới.

Nói ngắn gọn:

- `env` chỉ dùng để bootstrap và seed/overwrite khi service khởi động;
- database mới là nơi quyết định key active ở runtime.

## Thiết kế dữ liệu mục tiêu

## Bảng chính

Đề xuất thêm bảng dùng chung cho từng service, ví dụ `api_credentials`.

Field tối thiểu:

- `Id`
- `ServiceName`
  - ví dụ: `User`, `Ai`
- `Provider`
  - ví dụ: `Stripe`, `Facebook`, `Gemini`, `Kie`, `Threads`, `TikTok`
- `KeyName`
  - ví dụ: `SecretKey`, `WebhookSecret`, `AppSecret`, `ApiKey`
- `DisplayName`
- `ValueEncrypted`
- `ValueLast4`
- `IsActive`
- `Source`
  - `env_seeded`
  - `admin_created`
  - `admin_updated`
- `Version`
- `LastSyncedFromEnvAt`
- `LastRotatedAt`
- `CreatedByUserId`
- `UpdatedByUserId`
- `CreatedAt`
- `UpdatedAt`
- `DeletedAt`
- `IsDeleted`

Ràng buộc tối thiểu:

- unique theo `ServiceName + Provider + KeyName`

Điều này bảo đảm logic "trùng tên thì ghi đè" không tạo duplicate record.

## Bảng lịch sử

Đề xuất thêm `api_credential_revisions` hoặc audit log tương đương để lưu:

- `CredentialId`
- `OldVersion`
- `NewVersion`
- `ChangeType`
  - `env_sync`
  - `create`
  - `update`
  - `rotate`
  - `deactivate`
- `ChangedByUserId`
- `ChangedAt`

Không nên chỉ update đè mà không có audit trail, vì đây là khu vực nhạy cảm.

## Quy tắc lưu secret

Không lưu plaintext trực tiếp trong cột business bình thường.

Yêu cầu:

- `ValueEncrypted` phải được mã hóa trước khi lưu;
- response API mặc định chỉ trả `maskedValue`;
- log tuyệt đối không ghi raw secret;
- migration/test data/screenshot/docs không được chứa giá trị secret thật.

## Luồng đồng bộ `env -> DB`

### 1. Khi nào chạy

Luồng sync chạy khi service startup, sau khi đã có kết nối database.

Pattern phù hợp với repo hiện tại:

- đăng ký một startup seeder hoặc hosted startup task ở `Infrastructure`/`WebApi`;
- mỗi service tự khai báo danh sách key mà nó quản lý;
- task chạy `upsert` từng key vào DB.

### 2. Logic đồng bộ

Với mỗi key:

1. đọc giá trị từ `IConfiguration`;
2. nếu rỗng thì bỏ qua;
3. tìm bản ghi theo `ServiceName + Provider + KeyName`;
4. nếu chưa có thì tạo mới;
5. nếu đã có thì cập nhật giá trị và metadata;
6. tăng `Version`;
7. ghi `Source = env_seeded`;
8. cập nhật `LastSyncedFromEnvAt`.

### 3. Ghi đè khi trùng tên

Requirement đã chốt rõ:

- nếu `env` có key trùng tên với key trong DB thì phải ghi đè bằng giá trị từ `env` trong lần startup đó.

Điều này đặc biệt hữu ích khi:

- deploy môi trường mới;
- rotate secret ở hạ tầng trước;
- cần đảm bảo service boot bằng bộ key đúng với môi trường.

Tuy nhiên sau khi startup xong, các update tiếp theo của admin trong DB vẫn là giá trị runtime mới nhất mà nghiệp vụ phải dùng.

## API hiện có trong repo

Các API quản lý credential đã được triển khai riêng trong từng service sở hữu credential. Không gọi chéo sang service khác để ghi key.

Base routes hiện tại:

- `User.Microservice`: `/api/User/admin/api-keys`
- `Ai.Microservice`: `/api/Ai/admin/api-keys`

Tất cả response thành công giữ envelope hiện có:

```json
{
  "value": { },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

Khi fail, controller dùng `HandleFailure(result)`, trả `ProblemDetails` theo contract chung của backend.

### Authorization

Các route này là admin-only:

- `User.Microservice`: `[Authorize("ADMIN", "Admin")]`
- `Ai.Microservice`: `[Authorize("ADMIN", "Admin", "admin")]`

Non-admin không được xem hoặc chỉnh key. FE nên xử lý `401/403` theo contract auth hiện có của service, không tự suy diễn từ body thành công.

### Response DTO

Mọi endpoint trả `ApiCredentialResponse` hoặc list của DTO này. Raw secret không bao giờ được trả về.

```json
{
  "id": "8d7d90bb-66bb-4f67-a5d7-8598b07fd5b6",
  "serviceName": "Ai",
  "provider": "Gemini",
  "keyName": "ApiKey",
  "displayName": "Gemini API key",
  "maskedValue": "****abcd",
  "isActive": true,
  "source": "admin_updated",
  "version": 3,
  "lastSyncedFromEnvAt": "2026-04-23T08:30:00Z",
  "lastRotatedAt": "2026-04-23T09:00:00Z",
  "createdAt": "2026-04-23T08:30:00Z",
  "updatedAt": "2026-04-23T09:00:00Z"
}
```

Field semantics:

- `id`: credential row id, dùng cho update.
- `serviceName`: service sở hữu credential, hiện là `User` hoặc `Ai`.
- `provider`: nhóm provider, ví dụ `Stripe`, `Facebook`, `Gemini`.
- `keyName`: tên key trong provider, ví dụ `SecretKey`, `ApiKey`.
- `displayName`: label cho UI admin.
- `maskedValue`: chỉ hiển thị dạng mask dựa trên `ValueLast4`; không phải raw secret.
- `isActive`: `false` nghĩa là runtime provider không nên trả credential này cho nghiệp vụ.
- `source`: `env_seeded`, `admin_created`, hoặc `admin_updated`.
- `version`: tăng khi startup sync hoặc admin update.
- `lastSyncedFromEnvAt`: lần gần nhất service seed/overwrite từ env.
- `lastRotatedAt`: lần gần nhất value được thay đổi.
- `createdAt`, `updatedAt`: audit timestamp cơ bản.

### GET danh sách key

Routes:

- `GET /api/User/admin/api-keys`
- `GET /api/Ai/admin/api-keys`

Query params:

- `provider` optional, exact match sau khi trim, ví dụ `Stripe`, `Gemini`.
- `keyName` optional, exact match sau khi trim, ví dụ `SecretKey`, `ApiKey`.
- `isActive` optional boolean, ví dụ `true` hoặc `false`.

Response type:

- `200 OK`
- Body: `Result<IReadOnlyList<ApiCredentialResponse>>`

Ví dụ request:

```bash
curl -X GET "http://localhost:2406/api/Ai/admin/api-keys?provider=Gemini&isActive=true" \
  -H "Authorization: Bearer <admin-token>"
```

Ví dụ response:

```json
{
  "value": [
    {
      "id": "019dbc11-3f6f-7110-bf4f-83d2f8fd8e02",
      "serviceName": "Ai",
      "provider": "Gemini",
      "keyName": "ApiKey",
      "displayName": "Gemini API key",
      "maskedValue": "****1234",
      "isActive": true,
      "source": "env_seeded",
      "version": 1,
      "lastSyncedFromEnvAt": "2026-04-23T08:30:00Z",
      "lastRotatedAt": "2026-04-23T08:30:00Z",
      "createdAt": "2026-04-23T08:30:00Z",
      "updatedAt": "2026-04-23T08:30:00Z"
    }
  ],
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

### POST thêm key mới

Routes:

- `POST /api/User/admin/api-keys`
- `POST /api/Ai/admin/api-keys`

Request DTO:

```json
{
  "provider": "Stripe",
  "keyName": "WebhookSecret",
  "displayName": "Stripe webhook secret",
  "value": "whsec_xxx",
  "isActive": true
}
```

Validation hiện tại:

- `provider` required, không được whitespace.
- `keyName` required, không được whitespace.
- `value` required, không được whitespace.
- `displayName` optional; nếu rỗng thì backend dùng default `"{provider} {keyName}"`.
- `isActive` optional ở JSON; default record là `true` nếu omitted.

Behavior:

- Chỉ tạo mới nếu chưa có row active/non-deleted cùng `serviceName + provider + keyName` trong service hiện tại.
- Nếu đã tồn tại, trả failure `ApiCredential.AlreadyExists`.
- Backend encrypt `value`, lưu `valueLast4`, set `source = "admin_created"`, `version = 1`, `lastRotatedAt = now`.
- Sau khi save DB, credential provider được cập nhật in-memory bằng value mới nếu `isActive = true`; nếu `isActive = false`, provider lưu null cho key đó.

Response type:

- `200 OK`
- Body: `Result<ApiCredentialResponse>`

Error cases chính:

- `ApiCredential.InvalidRequest`: thiếu `provider`, `keyName`, hoặc `value`.
- `ApiCredential.AlreadyExists`: key đã tồn tại trong service hiện tại.

Ví dụ tạo Gemini key:

```bash
curl -X POST "http://localhost:2406/api/Ai/admin/api-keys" \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  --data-binary @- <<'JSON'
{
  "provider": "Gemini",
  "keyName": "ApiKey",
  "displayName": "Gemini API key",
  "value": "<new-secret>",
  "isActive": true
}
JSON
```

### PUT cập nhật metadata, bật/tắt, hoặc rotate value

Routes:

- `PUT /api/User/admin/api-keys/{id}`
- `PUT /api/Ai/admin/api-keys/{id}`

Request DTO:

```json
{
  "displayName": "Gemini primary key",
  "value": "new-secret",
  "isActive": true
}
```

Tất cả fields đều optional:

- `displayName`: nếu non-empty thì cập nhật label.
- `value`: nếu non-empty thì rotate secret value.
- `isActive`: nếu có thì bật/tắt key.

Behavior:

- Chỉ tìm credential trong service hiện tại; `Ai` không update được row của `User` và ngược lại.
- Nếu không tìm thấy row non-deleted, trả `ApiCredential.NotFound`.
- Nếu `value` có giá trị, backend encrypt lại, cập nhật `valueLast4`, `lastRotatedAt` và runtime provider.
- Luôn set `source = "admin_updated"`, tăng `version`, cập nhật `updatedAt`.
- Nếu sau update `isActive = false`, runtime provider lưu null cho key đó.
- Nếu `isActive = true` và request có `value`, runtime provider dùng ngay value mới.
- Nếu `isActive = true` nhưng request không có `value`, runtime provider bị invalidate để lần đọc sau resolve lại từ DB.

Response type:

- `200 OK`
- Body: `Result<ApiCredentialResponse>`

Error cases chính:

- `ApiCredential.NotFound`: id không tồn tại, đã xóa, hoặc không thuộc service hiện tại.

Ví dụ rotate Stripe secret key:

```bash
curl -X PUT "http://localhost:2406/api/User/admin/api-keys/<credential-id>" \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  --data-binary @- <<'JSON'
{
  "value": "<new-stripe-secret-key>",
  "isActive": true
}
JSON
```

Ví dụ disable key:

```bash
curl -X PUT "http://localhost:2406/api/Ai/admin/api-keys/<credential-id>" \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  --data-binary '{ "isActive": false }'
```

### Route rotate riêng

Hiện repo chưa có endpoint riêng:

- chưa có `PUT /api/User/admin/api-keys/{id}/rotate`;
- chưa có `PUT /api/Ai/admin/api-keys/{id}/rotate`.

Rotate hiện được thực hiện bằng `PUT /api/{Service}/admin/api-keys/{id}` với body có `value` mới. Nếu FE muốn nút "Rotate", hãy gọi endpoint `PUT {id}` và chỉ gửi `value` + `isActive` nếu cần.

Nếu sau này cần audit phân biệt metadata update và rotate rõ hơn, có thể thêm route semantic wrapper `/rotate`, nhưng phải giữ endpoint `PUT {id}` backward-compatible.

## Catalog key theo service hiện tại

### `User.Microservice`

Base route:

- `/api/User/admin/api-keys`

`serviceName` trong DB:

- `User`

Catalog sync từ env hiện tại:

| Provider | KeyName | Display name | Config key | Env key |
| --- | --- | --- | --- | --- |
| `Stripe` | `PublishableKey` | Stripe publishable key | `Stripe:PublishableKey` | `Stripe__PublishableKey` |
| `Stripe` | `SecretKey` | Stripe secret key | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `Stripe` | `WebhookSecret` | Stripe webhook secret | `Stripe:WebhookSecret` | `Stripe__WebhookSecret` |
| `Facebook` | `AppId` | Facebook app id | `Facebook:AppId` | `Facebook__AppId` |
| `Facebook` | `AppSecret` | Facebook app secret | `Facebook:AppSecret` | `Facebook__AppSecret` |
| `Instagram` | `AppId` | Instagram app id | `Instagram:AppId` | `Instagram__AppId` |
| `Instagram` | `AppSecret` | Instagram app secret | `Instagram:AppSecret` | `Instagram__AppSecret` |
| `TikTok` | `ClientKey` | TikTok client key | `TikTok:ClientKey` | `TikTok__ClientKey` |
| `TikTok` | `ClientSecret` | TikTok client secret | `TikTok:ClientSecret` | `TikTok__ClientSecret` |
| `Threads` | `AppId` | Threads app id | `Threads:AppId` | `Threads__AppId` |
| `Threads` | `AppSecret` | Threads app secret | `Threads:AppSecret` | `Threads__AppSecret` |

Runtime consumers already using credential provider include Stripe payment/webhook paths and social OAuth services for Facebook, Instagram, TikTok, and Threads.

### `Ai.Microservice`

Base route:

- `/api/Ai/admin/api-keys`

`serviceName` trong DB:

- `Ai`

Catalog sync từ env hiện tại:

| Provider | KeyName | Display name | Config key | Env key |
| --- | --- | --- | --- | --- |
| `Gemini` | `ApiKey` | Gemini API key | `Gemini:ApiKey` | `Gemini__ApiKey` |
| `Kie` | `ApiKey` | Kie API key | `Kie:ApiKey` | `Kie__ApiKey` |
| `N8n` | `InternalCallbackToken` | n8n internal callback token | `N8n:InternalCallbackToken` | `N8n__InternalCallbackToken` |

Runtime consumers already using credential provider include Gemini agent/caption/runtime generation flows, Kie/Veo services, and n8n callback token generation.

## Runtime sync và provider hiện tại

Startup sync:

- `User.Microservice` gọi `ApiCredentialSyncSeeder` trong `WebApi/Setups/SeedingSetup.cs`.
- `Ai.Microservice` gọi `ApiCredentialSyncSeeder` trong `WebApi/Setups/SeedingSetup.cs`.
- Seeder đọc catalog của service, lấy value từ `IConfiguration`, skip value rỗng, upsert vào `api_credentials`.
- Nếu env có key trùng DB, startup sync ghi đè value, set `source = "env_seeded"`, `isActive = true`, cập nhật `lastSyncedFromEnvAt`, và tăng `version` khi value đổi.

Runtime provider:

- Abstraction: `IApiCredentialProvider`.
- Methods: `GetRequiredValue(provider, keyName)`, `GetOptionalValue(provider, keyName)`, `StoreValue(provider, keyName, value)`, `Invalidate(provider, keyName)`.
- Admin create/update gọi `StoreValue` hoặc `Invalidate` để request sau dùng credential mới.
- Raw secret được decrypt trong provider khi cần dùng, không trả qua API response.

## Dữ liệu và bảo mật hiện tại

Bảng `api_credentials` hiện có các field chính:

- `id`
- `service_name`
- `provider`
- `key_name`
- `display_name`
- `value_encrypted`
- `value_last4`
- `is_active`
- `source`
- `version`
- `last_synced_from_env_at`
- `last_rotated_at`
- `created_at`
- `updated_at`
- `deleted_at`
- `is_deleted`

Unique index:

- `service_name + provider + key_name`

Không có bảng revision/audit riêng trong implementation hiện tại. Audit hiện chỉ ở metadata row (`source`, `version`, timestamps). Nếu cần audit đầy đủ từng lần rotate/update, phải bổ sung bảng revision sau.

Raw secret rule:

- Request `POST/PUT` nhận plaintext `value`.
- Backend encrypt trước khi lưu.
- Response chỉ trả `maskedValue`.
- Không log raw secret.

## Rule mở rộng cho service mới

Nếu sau này thêm microservice mới và service đó có credential trong `env`, service mới cũng phải:

- khai báo danh sách key được sync;
- sync `env -> DB` khi startup;
- expose endpoint admin riêng cho service đó;
- dùng `IApiCredentialProvider` hoặc abstraction tương đương khi gọi nghiệp vụ cần secret.

## Phi chức năng và an toàn

- Không trả raw secret trong API list/detail mặc định.
- Không log raw secret.
- Không commit secret thật vào repo, docs, hay test snapshot.
- Audit tối thiểu bằng `source`, `version`, `lastSyncedFromEnvAt`, `lastRotatedAt`, `createdAt`, `updatedAt`; nếu cần audit đầy đủ từng lần đổi, bổ sung bảng revision riêng.
- Hỗ trợ soft delete hoặc deactivate thay vì hard delete để tránh mất trace.
- Nên có optimistic concurrency bằng `Version` để tránh admin ghi đè lẫn nhau.

## Tiêu chí hoàn thành cho FR-A5

`FR-A5` được coi là hoàn thành khi đáp ứng đủ các điều kiện sau:

1. Mỗi service có key trong `env` đều tự sync các key đó lên database lúc startup.
2. Bản ghi trùng `ServiceName + Provider + KeyName` luôn bị ghi đè bởi giá trị từ `env` trong bước sync startup.
3. Admin có thể xem, thêm mới, chỉnh sửa, bật/tắt, và rotate key qua `PUT /api/{Service}/admin/api-keys/{id}`.
4. Toàn bộ endpoint quản lý key đều bị chặn cho non-admin.
5. Các nghiệp vụ gọi social network, Stripe, hoặc AI provider luôn resolve key mới nhất từ database/runtime credential provider.
6. Không còn điểm gọi provider quan trọng nào phụ thuộc cứng vào key đã đọc từ `env` lúc startup mà không có cơ chế refresh.

## Kết luận

`FR-A5` không chỉ là thêm một CRUD cho secret. Đây là thay đổi source-of-truth của credential runtime:

- `env` dùng để bootstrap;
- database dùng để quản trị;
- admin là người được phép vận hành;
- runtime phải luôn dùng key mới nhất mà admin đã cập nhật.

Nếu thiếu ý cuối cùng, phần "API key management" sẽ chỉ là giao diện lưu dữ liệu, nhưng không thực sự kiểm soát hành vi của hệ thống.

---

# Phần 2 — Frontend Integration

## Mục tiêu

Tài liệu này hướng dẫn frontend triển khai màn hình quản lý API key cho `FR-A5` dựa trên backend đã có trong repo hiện tại.

Phạm vi:

- trang admin để xem danh sách API key của từng service;
- form thêm mới key;
- form chỉnh sửa key;
- bật/tắt `isActive`;
- hiển thị trạng thái masked secret và metadata;
- dùng đúng contract hiện có của backend, không bịa thêm field hay route.

## 1. Tổng quan FE flow

Frontend nên xem `FR-A5` là một admin module có 2 nhóm dữ liệu tách riêng:

- `User.Microservice` credentials
- `Ai.Microservice` credentials

UI đơn giản nhất:

1. admin chọn service cần quản lý;
2. FE gọi API list của service đó;
3. FE render bảng key hiện có;
4. admin có thể mở modal hoặc drawer để:
   - thêm key mới
   - chỉnh sửa key hiện có
   - bật/tắt key
5. sau khi save thành công, FE refetch danh sách để đồng bộ state.

Khuyến nghị:

- không giữ local optimistic state quá lâu cho secret data;
- sau mỗi thao tác create/update nên refetch;
- không cache lâu ở browser cho trang này.

## 2. Backend APIs thực tế

## 2.1. User service

Base route:

- `GET /api/User/admin/api-keys`
- `POST /api/User/admin/api-keys`
- `PUT /api/User/admin/api-keys/{id}`

Source:

- `Backend/Microservices/User.Microservice/src/WebApi/Controllers/AdminApiKeysController.cs`

## 2.2. AI service

Base route:

- `GET /api/Ai/admin/api-keys`
- `POST /api/Ai/admin/api-keys`
- `PUT /api/Ai/admin/api-keys/{id}`

Source:

- `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/AdminApiKeysController.cs`

## 2.3. Gateway usage

Frontend nên gọi qua API Gateway theo pattern routing hiện có của hệ thống.

Nếu frontend đang dùng cùng base URL gateway như các module khác, chỉ cần đổi path:

- `/api/User/admin/api-keys`
- `/api/Ai/admin/api-keys`

## 3. Authentication và authorization

Các endpoint này là `admin-only`.

Role backend đang chấp nhận:

- `User.Microservice`: `ADMIN`, `Admin`
- `Ai.Microservice`: `ADMIN`, `Admin`, `admin`

Kỳ vọng FE:

- chỉ hiển thị menu/trang này cho admin;
- nếu backend trả `401`, điều hướng về login hoặc refresh session theo flow hiện có;
- nếu backend trả `403`, hiển thị thông báo không đủ quyền;
- không cố che lỗi quyền bằng UI fallback mơ hồ.

## 4. Response contract

Backend đang trả `Result<T>` giống các admin API khác trong hệ thống.

Response item hiện có:

```ts
export type ApiCredentialItem = {
  id: string;
  serviceName: "User" | "Ai";
  provider: string;
  keyName: string;
  displayName: string;
  maskedValue: string;
  isActive: boolean;
  source: string;
  version: number;
  lastSyncedFromEnvAt: string | null;
  lastRotatedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
};

export type Result<T> = {
  isSuccess: boolean;
  isFailure: boolean;
  error: {
    code: string;
    description: string;
  };
  value: T;
};
```

Lưu ý:

- `maskedValue` chỉ là giá trị che một phần, FE không có raw secret cũ để hiển thị lại;
- đây là hành vi đúng, không phải bug;
- khi edit một key hiện có, ô nhập secret phải để trống mặc định.

## 5. Query params của API list

`GET` hiện hỗ trợ:

- `provider?: string`
- `isActive?: boolean`
- `keyName?: string`

Ví dụ:

- `/api/User/admin/api-keys?provider=Stripe`
- `/api/Ai/admin/api-keys?isActive=true`
- `/api/Ai/admin/api-keys?provider=Gemini&keyName=ApiKey`

FE nên map các filter này thành:

- dropdown `Provider`
- toggle hoặc select `Active / Inactive / All`
- search input cho `Key Name`

## 6. Request payloads

## 6.1. Tạo key mới

Payload `POST`:

```ts
export type CreateApiCredentialRequest = {
  provider: string;
  keyName: string;
  displayName?: string | null;
  value: string;
  isActive?: boolean;
};
```

Ví dụ:

```json
{
  "provider": "Gemini",
  "keyName": "ApiKey",
  "displayName": "Gemini production key",
  "value": "AIza....",
  "isActive": true
}
```

## 6.2. Chỉnh sửa key

Payload `PUT`:

```ts
export type UpdateApiCredentialRequest = {
  displayName?: string | null;
  value?: string | null;
  isActive?: boolean | null;
};
```

Ví dụ chỉ đổi tên:

```json
{
  "displayName": "Gemini primary key"
}
```

Ví dụ rotate secret:

```json
{
  "value": "AIza....new"
}
```

Ví dụ deactivate:

```json
{
  "isActive": false
}
```

## 7. Gợi ý UI structure

## 7.1. Layout đề xuất

Một layout đủ dùng:

- tabs hoặc segmented control để chuyển giữa `User` và `Ai`
- filter row phía trên bảng
- data table danh sách keys
- nút `Add key`
- nút `Edit` trên từng row
- status badge cho `isActive`
- metadata phụ cho `source`, `version`, `updatedAt`

## 7.2. Columns đề xuất

- `Provider`
- `Key Name`
- `Display Name`
- `Masked Value`
- `Status`
- `Source`
- `Version`
- `Last Rotated`
- `Last Synced From Env`
- `Updated At`
- `Actions`

## 7.3. Row actions

- `Edit`
- `Activate / Deactivate`

Hiện backend chưa có endpoint `rotate` riêng, nên FE có thể:

- dùng cùng form edit;
- nếu admin nhập `value` mới thì xem đó là rotate.

## 8. Form behavior bắt buộc

## 8.1. Create form

Field nên có:

- `provider`
- `keyName`
- `displayName`
- `value`
- `isActive`

Validation FE tối thiểu:

- `provider` bắt buộc
- `keyName` bắt buộc
- `value` bắt buộc

## 8.2. Edit form

Field nên có:

- `displayName`
- `value`
- `isActive`

Rule:

- `value` để trống mặc định;
- nếu admin không nhập `value`, FE chỉ gửi field cần update khác;
- không đổ `maskedValue` ngược vào input secret;
- nếu admin paste secret mới thì FE gửi `value` mới.

## 8.3. UX copy gợi ý

- label input secret mới: `New secret value`
- helper text: `Leave empty if you do not want to rotate this key`
- helper text trạng thái: `Inactive keys will not be used by runtime flows`

## 9. Rendering states

Frontend nên xử lý rõ các state sau:

- `loading`
- `empty`
- `error`
- `saving`
- `save success`

Ví dụ:

- loading table skeleton khi gọi list;
- empty state riêng nếu service chưa có key nào;
- inline error từ `ProblemDetails.detail` khi create/update fail;
- disable submit button trong lúc save.

## 10. Error handling

Backend đang trả lỗi business qua `ProblemDetails`.

FE nên đọc:

- `status`
- `type`
- `detail`
- `errors`

Một số case thực tế:

- `ApiCredential.InvalidRequest`
- `ApiCredential.AlreadyExists`
- `ApiCredential.NotFound`

Mapping UI gợi ý:

- `AlreadyExists` -> báo trùng key, giữ form mở;
- `InvalidRequest` -> highlight field tương ứng nếu xác định được;
- `NotFound` -> đóng modal và refetch list;
- `401/403` -> xử lý theo auth flow admin.

## 11. TypeScript client gợi ý

```ts
export type ServiceKind = "User" | "Ai";

export function getApiKeyPath(service: ServiceKind) {
  return service === "User"
    ? "/api/User/admin/api-keys"
    : "/api/Ai/admin/api-keys";
}

export async function listApiKeys(
  service: ServiceKind,
  query?: {
    provider?: string;
    isActive?: boolean;
    keyName?: string;
  }
) {
  const params = new URLSearchParams();

  if (query?.provider) params.set("provider", query.provider);
  if (query?.isActive !== undefined) params.set("isActive", String(query.isActive));
  if (query?.keyName) params.set("keyName", query.keyName);

  const url = `${getApiKeyPath(service)}${params.size > 0 ? `?${params}` : ""}`;
  const response = await fetch(url, {
    method: "GET",
    credentials: "include"
  });

  if (!response.ok) {
    throw await response.json();
  }

  return response.json() as Promise<Result<ApiCredentialItem[]>>;
}

export async function createApiKey(
  service: ServiceKind,
  payload: CreateApiCredentialRequest
) {
  const response = await fetch(getApiKeyPath(service), {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw await response.json();
  }

  return response.json() as Promise<Result<ApiCredentialItem>>;
}

export async function updateApiKey(
  service: ServiceKind,
  id: string,
  payload: UpdateApiCredentialRequest
) {
  const response = await fetch(`${getApiKeyPath(service)}/${id}`, {
    method: "PUT",
    credentials: "include",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw await response.json();
  }

  return response.json() as Promise<Result<ApiCredentialItem>>;
}
```

## 12. React state gợi ý

Nếu dùng React Query hoặc SWR:

- query key: `["admin-api-keys", service, filters]`
- mutation create xong: invalidate query list của service tương ứng
- mutation update xong: invalidate query list của service tương ứng

Nếu dùng state thường:

- save thành công thì gọi lại API list;
- không nên tự merge local bằng giả định vì backend có thể tăng `version`, đổi `source`, hoặc cập nhật `updatedAt`.

## 13. Danh sách provider nên chuẩn bị trên FE

## 13.1. User service

Provider hiện backend đã seed/sync:

- `Stripe`
- `Facebook`
- `Instagram`
- `TikTok`
- `Threads`

## 13.2. AI service

Provider hiện backend đã seed/sync:

- `Gemini`
- `Kie`
- `N8n`

Khuyến nghị:

- FE không hardcode quá cứng danh sách provider trong logic;
- có thể hardcode cho dropdown ban đầu, nhưng vẫn phải render được item lạ từ backend nếu sau này có provider mới.

## 14. Những điều FE không nên làm

- Không hiển thị lại raw secret cũ.
- Không dùng `maskedValue` làm giá trị edit.
- Không assume `serviceName` luôn trùng tab đang mở nếu tái sử dụng component.
- Không swallow `403` thành empty state.
- Không lưu secret draft vào URL query string.
- Không log secret ở console, analytics, hoặc error tracking.

## 15. Checklist hoàn thành frontend

Frontend được coi là xong phần `FR-A5` khi:

1. Admin xem được list API key của `User` và `Ai`.
2. Admin filter được theo `provider`, `keyName`, `isActive`.
3. Admin thêm mới một key qua form.
4. Admin chỉnh sửa metadata hoặc secret của một key qua form.
5. Admin bật/tắt `isActive`.
6. UI không bao giờ hiển thị raw secret đã lưu trước đó.
7. UI xử lý đúng `401`, `403`, business errors, loading state, và refetch sau save.

## 16. Gợi ý mở rộng sau này

Nếu backend bổ sung thêm các tính năng sau, FE có thể mở rộng tiếp:

- endpoint `rotate` riêng;
- audit trail lịch sử đổi key;
- detail drawer cho từng version;
- confirm dialog khi deactivate key đang active;
- badge cảnh báo key vừa bị env sync ghi đè ở lần startup gần nhất.

---

# Checklist tổng hợp cuối cùng

FR-A5 được coi là hoàn chỉnh end-to-end khi cả backend và frontend cùng đáp ứng các điểm sau:

1. Mỗi service có credential trong `env` đều tự sync credential của chính service đó lên database khi startup.
2. Bản ghi trùng `ServiceName + Provider + KeyName` được upsert và ghi đè bằng giá trị từ `env` trong startup sync.
3. Database là runtime source of truth; code nghiệp vụ không tiếp tục phụ thuộc cứng vào key đọc từ `env` ở constructor.
4. Admin có thể xem list key theo service, filter theo `provider`, `keyName`, `isActive`.
5. Admin có thể thêm mới key bằng `POST /api/{Service}/admin/api-keys`.
6. Admin có thể cập nhật metadata, bật/tắt key hoặc rotate secret bằng `PUT /api/{Service}/admin/api-keys/{id}`.
7. Tất cả endpoint quản lý key là admin-only và xử lý đúng `401/403`.
8. Response API không bao giờ trả raw secret; FE chỉ hiển thị `maskedValue`.
9. Form edit không đổ `maskedValue` vào ô secret; nếu để trống thì không rotate.
10. Sau mỗi create/update, FE refetch list để lấy lại `version`, `source`, `updatedAt`, `maskedValue` mới.
11. Không log raw secret ở backend, frontend, console, analytics, error tracking, docs hoặc test snapshot.
12. Nếu sau này thêm service mới có credential riêng, service đó phải có catalog sync, admin endpoint riêng và runtime credential provider tương ứng.
