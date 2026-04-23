# FR-A5 API Key Management

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

Ví dụ endpoint mục tiêu:

- `User.Microservice`
  - `GET /api/User/admin/api-keys`
  - `POST /api/User/admin/api-keys`
  - `PUT /api/User/admin/api-keys/{id}`
  - `PUT /api/User/admin/api-keys/{id}/rotate`
- `Ai.Microservice`
  - `GET /api/Ai/admin/api-keys`
  - `POST /api/Ai/admin/api-keys`
  - `PUT /api/Ai/admin/api-keys/{id}`
  - `PUT /api/Ai/admin/api-keys/{id}/rotate`

Service nào không có secret/key riêng thì không cần endpoint này.

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

## Thiết kế API mục tiêu

## Response shape

Vì repo đang ưu tiên `Result` / `Result<T>` và `HandleFailure(result)`, API mới nên giữ cùng response contract.

Response item gợi ý:

```json
{
  "id": "8d7d90bb-66bb-4f67-a5d7-8598b07fd5b6",
  "serviceName": "Ai",
  "provider": "Gemini",
  "keyName": "ApiKey",
  "displayName": "Gemini API key",
  "maskedValue": "****abcd",
  "isActive": true,
  "version": 3,
  "source": "admin_updated",
  "lastSyncedFromEnvAt": "2026-04-23T08:30:00Z",
  "lastRotatedAt": "2026-04-23T09:00:00Z",
  "updatedAt": "2026-04-23T09:00:00Z"
}
```

## GET danh sách key

Ví dụ:

- `GET /api/User/admin/api-keys`
- `GET /api/Ai/admin/api-keys`

Cho phép filter theo:

- `provider`
- `isActive`
- `keyName`

## POST thêm key mới

Ví dụ request:

```json
{
  "provider": "Stripe",
  "keyName": "WebhookSecret",
  "displayName": "Stripe webhook secret",
  "value": "whsec_xxx",
  "isActive": true
}
```

Rule:

- nếu key chưa tồn tại thì tạo mới;
- nếu key đã tồn tại thì hoặc trả lỗi duplicate, hoặc treat như upsert. Với `FR-A5`, hướng an toàn hơn là chỉ `POST` cho create mới và để `PUT` xử lý update.

## PUT chỉnh sửa key

Ví dụ:

```json
{
  "displayName": "Gemini primary key",
  "value": "new-secret",
  "isActive": true
}
```

Rule:

- admin có thể đổi `displayName`;
- admin có thể thay đổi secret value;
- cập nhật phải tăng `Version`;
- ghi audit log;
- mọi request sau đó phải dùng version mới.

## PUT rotate key

Endpoint rotate có thể là semantic wrapper cho update value:

- `PUT /api/{service}/admin/api-keys/{id}/rotate`

Mục đích:

- giúp log/audit phân biệt giữa update metadata và rotate credential;
- thuận lợi cho UI admin.

## Phân quyền chi tiết

Mọi endpoint trên phải gắn:

- `[Authorize("ADMIN", "Admin")]`

hoặc một admin policy tương đương, nhưng phải giữ tương thích với role naming hiện có trong repo.

## Thiết kế runtime để luôn dùng key mới nhất

Đây là phần bắt buộc để `FR-A5` có giá trị thực tế.

## Anti-pattern cần tránh

Sau khi có `FR-A5`, không nên tiếp tục các pattern sau cho những key được quản lý bởi admin:

- đọc key một lần trong constructor rồi giữ ở field private suốt vòng đời service;
- bind secret vào singleton options và không bao giờ refresh;
- gọi provider bằng giá trị trực tiếp từ `IConfiguration` nếu key đó đã nằm trong bảng quản lý.

## Pattern nên dùng

Thêm abstraction kiểu:

- `IApiCredentialProvider`
- `IApiCredentialRepository`
- `IApiCredentialEncryptionService`

Trách nhiệm:

- `IApiCredentialProvider` trả về key active mới nhất theo `ServiceName + Provider + KeyName`;
- provider có thể dùng cache ngắn hạn, nhưng phải có invalidation khi admin update;
- mọi nghiệp vụ gọi provider ngoài hệ thống phải resolve credential thông qua abstraction này.

Ví dụ các nơi cần chuyển sang đọc từ DB/runtime provider:

- `StripePaymentService`
- `StripeWebhooksController`
- `FacebookOAuthService`
- `InstagramOAuthService`
- `TikTokOAuthService`
- `ThreadsOAuthService`
- `GeminiCaptionService`
- `GeminiContentModerationService`
- `KieCaptionService`
- `KieImageService`
- `VeoVideoService`
- `AgenticRuntimeContentService`
- `GeminiAgentChatService`

## Thứ tự ưu tiên giá trị

Để tránh mơ hồ, runtime precedence phải là:

1. key active mới nhất trong database;
2. nếu chưa có bản ghi trong database, fallback sang `env` chỉ cho lần bootstrap tương thích ngược;
3. sau khi sync startup hoàn tất, các flow bình thường phải đọc từ database/provider.

Nếu service không resolve được key active thì phải fail fast với lỗi rõ ràng thay vì gọi provider với secret cũ hoặc secret rỗng.

## Phạm vi theo service

## `User.Microservice`

Service này nên quản lý ít nhất:

- Stripe keys
- Meta app credentials cho Facebook/Instagram
- TikTok OAuth credentials
- Threads OAuth credentials
- các credential hạ tầng mà service dùng trực tiếp nếu muốn đưa vào cùng mô hình quản lý

Endpoint đề xuất:

- `GET /api/User/admin/api-keys`
- `POST /api/User/admin/api-keys`
- `PUT /api/User/admin/api-keys/{id}`
- `PUT /api/User/admin/api-keys/{id}/rotate`

## `Ai.Microservice`

Service này nên quản lý ít nhất:

- Gemini API key
- Kie API key
- Veo/Kie callback-related credentials nếu có
- n8n internal callback token
- các AI provider key khác được thêm sau này

Endpoint đề xuất:

- `GET /api/Ai/admin/api-keys`
- `POST /api/Ai/admin/api-keys`
- `PUT /api/Ai/admin/api-keys/{id}`
- `PUT /api/Ai/admin/api-keys/{id}/rotate`

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
- Audit mọi thao tác create/update/rotate.
- Hỗ trợ soft delete hoặc deactivate thay vì hard delete để tránh mất trace.
- Nên có optimistic concurrency bằng `Version` để tránh admin ghi đè lẫn nhau.

## Tiêu chí hoàn thành cho FR-A5

`FR-A5` được coi là hoàn thành khi đáp ứng đủ các điều kiện sau:

1. Mỗi service có key trong `env` đều tự sync các key đó lên database lúc startup.
2. Bản ghi trùng `ServiceName + Provider + KeyName` luôn bị ghi đè bởi giá trị từ `env` trong bước sync startup.
3. Admin có thể xem, thêm mới, chỉnh sửa, và rotate key qua endpoint admin của từng service.
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
