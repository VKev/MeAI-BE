# FR-A7 Resource Management

## Mục tiêu

FR-A7 quản lý tài nguyên lưu trữ media của hệ thống, tập trung vào ảnh/video đang lưu trong S3 hoặc storage tương thích S3.

- FR-A7.1 Admin xem và quản lý storage resources: dung lượng đã dùng theo user, workspace, type, plan, namespace và trạng thái cleanup.
- FR-A7.2 Admin cấu hình quota, retention và cleanup policy cho media đã lưu.
- FR-A7.3 Admin cấu hình dung lượng storage theo từng subscription plan; user đang active gói nào thì được dùng đúng quota storage của gói đó.
- FR-A7.4 Admin cấu hình dung lượng mặc định cho free users; mặc định ban đầu là `100 MB`.
- Hệ thống phải enforce quota khi user upload hoặc backend tạo media thay user.
- Khi user đã dùng hết hoặc vượt quota của gói hiện tại, mọi luồng tạo/thêm resource mới phải bị chặn trước khi ghi thêm object vào S3.
- Local dev phải tách object trong S3 chung bằng namespace riêng cho từng dev, kể cả khi PostgreSQL local bị xóa volume và migrate lại.

## Quyết định thiết kế

### Có nên lưu resource data vào PostgreSQL không?

Có. PostgreSQL phải là metadata source of truth cho business ownership, quota và admin query. S3 chỉ là blob store, không nên là nguồn chính để trả lời các câu hỏi như "user này đã dùng bao nhiêu dung lượng", "gói này còn bao nhiêu dung lượng", "file này thuộc workspace nào", "file này có được cleanup chưa".

Lý do:

- S3 list object theo prefix chậm, eventual, tốn request và không phù hợp để render admin dashboard.
- S3 object không biết user subscription, workspace, soft delete, resource type theo business.
- Quota cần check trước hoặc ngay trong upload flow; nếu chỉ scan S3 thì không đảm bảo concurrency.
- PostgreSQL local reset là vấn đề môi trường dev, không phải lý do bỏ metadata DB. Cách xử lý đúng là namespace S3 theo dev và có reconciliation/cleanup tool.

S3 vẫn là storage source of truth cho bytes thực tế của object. Vì vậy DB cần lưu `size_bytes`, `storage_key`, `bucket`, `region`, `namespace`, `etag/checksum` và có job đối soát với S3 khi cần.

## Namespace S3 cho từng dev

Vì cả team dùng chung S3 bucket nhưng mỗi người chạy PostgreSQL local riêng, mọi object phải nằm dưới một prefix namespace cố định từ env.

URL/prefix mong muốn:

```text
https://s3.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state/{namespace}
```

Trong code, `{namespace}` là prefix đầu tiên của S3 object key, không phải bucket mới. Ví dụ:

```text
bucket: vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state
namespace: khanghv-local
key: khanghv-local/resources/{userId}/{resourceId}
url: https://s3.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state/khanghv-local/resources/{userId}/{resourceId}
```

Env cần thêm cho `user-microservice` ở cả `docker-compose.yml` và `docker-compose-production.yml`:

```yaml
S3__Bucket: vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state
S3__Region: ap-southeast-1
S3__ServiceUrl: https://s3.ap-southeast-1.amazonaws.com
S3__Namespace: ${MEAI_S3_NAMESPACE:-local-vinhdo}
```

Quy tắc namespace:

- Required khi `S3__Bucket` trỏ vào bucket dùng chung.
- Chỉ cho phép `[a-z0-9][a-z0-9._-]{1,62}` để tránh path traversal và prefix xấu.
- Không cho phép namespace rỗng ở production-like compose.
- Key builder phải luôn tạo key dạng `{namespace}/resources/{userId}/{resourceId}`.
- Presign/delete/cleanup chỉ được thao tác object nằm trong namespace hiện tại, trừ endpoint admin super cleanup có xác nhận rõ namespace.

Khi dev xóa PostgreSQL volume:

- Object cũ vẫn còn trong S3 dưới namespace của dev.
- DB mới không còn biết ownership/quota của object cũ.
- Dev có thể chạy admin cleanup namespace để xóa toàn bộ prefix namespace hiện tại, hoặc chạy reconciliation để import lại metadata nếu cần giữ object.
- Không được dùng chung namespace giữa các dev, nếu không người này reset DB có thể mất khả năng quản lý object do người khác upload.

## Data model đề xuất

### Mở rộng bảng `resources`

Hiện `resources` đã có `id`, `user_id`, `workspace_id`, `link`, `type`, `content_type`, soft delete fields. Cần bổ sung:

| Column | Type | Ý nghĩa |
|---|---:|---|
| `storage_provider` | text | `s3`, sau này có thể là `r2`, `minio`, `wasabi` |
| `storage_bucket` | text | bucket chứa object |
| `storage_region` | text | region của bucket |
| `storage_namespace` | text | namespace từ env tại thời điểm tạo object |
| `storage_key` | text | object key đầy đủ, gồm namespace |
| `size_bytes` | bigint | dung lượng object dùng để tính quota |
| `original_file_name` | text nullable | tên file gốc, chỉ để admin/user xem |
| `etag` | text nullable | S3 ETag sau upload |
| `checksum_sha256` | text nullable | nếu có checksum |
| `storage_class` | text nullable | standard/ia/glacier nếu sau này lifecycle |
| `last_verified_at` | timestamptz nullable | lần cuối đối soát object còn tồn tại |
| `deleted_from_storage_at` | timestamptz nullable | hard delete khỏi S3 đã xong |
| `expires_at` | timestamptz nullable | thời điểm đủ điều kiện cleanup theo policy |

`link` có thể giữ để backward-compatible, nhưng code mới nên dùng `storage_key` làm canonical. `link` chỉ còn là legacy key/url field cho response cũ.

Index cần có:

- `ix_resources_user_namespace_deleted_created` trên `(user_id, storage_namespace, is_deleted, created_at)`.
- `ix_resources_namespace_key` unique trên `(storage_namespace, storage_key)`.
- `ix_resources_cleanup_due` trên `(storage_namespace, is_deleted, expires_at, deleted_from_storage_at)`.
- `ix_resources_user_size` trên `(user_id, is_deleted)` include `size_bytes` nếu cần tối ưu usage query.

### Bảng `user_storage_usages`

Dùng để enforce quota nhanh và chống race condition upload đồng thời.

| Column | Type |
|---|---:|
| `user_id` | uuid primary key |
| `storage_namespace` | text |
| `used_bytes` | bigint |
| `reserved_bytes` | bigint |
| `resource_count` | integer |
| `updated_at` | timestamptz |

Ý nghĩa:

- `used_bytes`: tổng dung lượng active tính quota.
- `reserved_bytes`: dung lượng đã giữ chỗ cho upload đang chạy.
- Upload check quota bằng row lock hoặc atomic update trên bảng này.
- Nếu upload fail, release reservation.
- Nếu upload success, chuyển reserved sang used và insert `resources`.

Có thể rebuild bảng này từ `resources` bằng admin reconciliation job.

### Mở rộng `SubscriptionLimits`

Hiện plan limits đang nằm trong JSONB `subscriptions.limits`. Cách phù hợp nhất là thêm field vào `SubscriptionLimits`:

```json
{
  "number_of_social_accounts": 8,
  "rate_limit_for_content_creation": 5,
  "number_of_workspaces": null,
  "max_pages_per_social_account": 10,
  "storage_quota_bytes": 10737418240,
  "max_upload_file_bytes": 524288000,
  "retention_days_after_delete": 30
}
```

Quy ước:

- `storage_quota_bytes = null`: unlimited.
- `storage_quota_bytes = 0`: không được upload media.
- `max_upload_file_bytes = null`: không giới hạn kích thước từng file ngoài quota tổng.
- `retention_days_after_delete = null`: dùng global default retention policy.
- Free tier default do admin cấu hình trong system config; seed mặc định là `100 MB` (`104857600` bytes).
- Admin chỉnh plan thì user đang dùng plan đó lấy quota mới ngay vì runtime đọc DB plan.

## Nơi setup và initial values

FR-A7 có 3 cấp storage setting khác nhau. Không dùng lẫn các cấp này:

| Cấp setting | Dùng cho ai | Admin API | DB/source |
|---|---|---|---|
| Free tier storage | User không có active subscription | `GET/PUT /api/User/admin/storage/settings/free-tier` | `configs.free_storage_quota_bytes` |
| Subscription plan storage | User có active subscription plan | `PATCH /api/User/admin/storage/plans/{subscriptionId}` hoặc `PATCH /api/User/admin/subscriptions/{subscriptionId}` | `subscriptions.limits.storage_quota_bytes` |
| System total storage | Tổng capacity toàn hệ thống | `GET/PUT /api/User/admin/storage/settings/system` | `configs.system_storage_quota_bytes` |

### Initial free tier storage

Giá trị mặc định hiện tại: `100 MB = 104857600` bytes.

Nơi khởi tạo:

- Entity default/migration config: `Backend/Microservices/User.Microservice/src/Infrastructure/Context/Configuration/ConfigConfiguration.cs`
- Seed config lần đầu: `Backend/Microservices/User.Microservice/src/Infrastructure/Logic/Seeding/ConfigSeeder.cs`
- Runtime fallback nếu DB chưa có config: `Backend/Microservices/User.Microservice/src/Application/Subscriptions/Services/IUserSubscriptionEntitlementService.cs`
- Migration đã tạo cột: `Backend/Microservices/User.Microservice/src/Infrastructure/Migrations/20260424052729_AddStorageQuotaManagement.cs`

Cách đổi initial cho môi trường mới/chưa seed DB:

1. Đổi `HasDefaultValue(104857600L)` trong `ConfigConfiguration.cs`.
2. Đổi `FreeStorageQuotaBytes = 100L * 1024L * 1024L` trong `ConfigSeeder.cs`.
3. Đổi `DefaultFreeStorageQuotaBytes` trong `IUserSubscriptionEntitlementService.cs` để fallback runtime khớp seed.
4. Tạo migration mới nếu DB schema default cần đổi.

Cách đổi khi DB đã chạy:

```http
PUT /api/User/admin/storage/settings/free-tier
Content-Type: application/json

{
  "freeStorageQuotaBytes": 209715200
}
```

Sau khi gọi API, free users dùng quota mới ngay trong lần upload tiếp theo. Không cần restart service.

### Initial subscription storage

Subscription plan storage nằm trong JSONB `subscriptions.limits`, không nằm trong `configs`.

Nơi khởi tạo plan seed:

- `Backend/Microservices/User.Microservice/src/Infrastructure/Logic/Seeding/SubscriptionSeeder.cs`

Giá trị seed hiện tại cho các plan mặc định:

```csharp
StorageQuotaBytes = 10L * 1024L * 1024L * 1024L, // 10 GB
MaxUploadFileBytes = 500L * 1024L * 1024L,       // 500 MB
RetentionDaysAfterDelete = 30
```

Cách đổi initial cho plan seed mới:

1. Đổi các field trên trong `SubscriptionSeeder.cs`.
2. Nếu database đã có plan cùng `Name`, seeder hiện tại sẽ skip plan đó; muốn đổi plan đã tồn tại thì dùng admin API hoặc migration/data script.

Cách admin đổi storage của một plan đang tồn tại:

```http
PATCH /api/User/admin/storage/plans/{subscriptionId}
Content-Type: application/json

{
  "storageQuotaBytes": 21474836480,
  "maxUploadFileBytes": 1073741824,
  "retentionDaysAfterDelete": 45
}
```

Hoặc dùng subscription API canonical:

```http
PATCH /api/User/admin/subscriptions/{subscriptionId}
Content-Type: application/json

{
  "limits": {
    "storage_quota_bytes": 21474836480,
    "max_upload_file_bytes": 1073741824,
    "retention_days_after_delete": 45
  }
}
```

Các user đang active trên plan đó nhận quota mới ngay ở lần upload tiếp theo.

### Initial system total storage

System total storage là quota tổng toàn hệ thống. Giá trị mặc định hiện tại là `null`, nghĩa là chưa giới hạn tổng hệ thống.

Nơi lưu:

- `configs.system_storage_quota_bytes`
- Cột được thêm bởi migration: `Backend/Microservices/User.Microservice/src/Infrastructure/Migrations/20260424055234_AddSystemStorageQuotaSetting.cs`

Cách admin set tổng capacity:

```http
PUT /api/User/admin/storage/settings/system
Content-Type: application/json

{
  "systemStorageQuotaBytes": 107374182400
}
```

`107374182400` bytes = `100 GB`.

Cách bỏ giới hạn tổng hệ thống:

```http
PUT /api/User/admin/storage/settings/system
Content-Type: application/json

{
  "systemStorageQuotaBytes": null
}
```

Rule bắt buộc:

- Subscription plan là nơi cấu hình storage entitlement chính. Không hard-code quota theo role hoặc theo user trong upload command.
- Khi user mua, gia hạn, downgrade hoặc admin đổi status subscription, hệ thống không copy quota vào user row; runtime luôn resolve active subscription -> plan -> `limits.storage_quota_bytes`.
- Khi admin chỉnh `storage_quota_bytes` của plan, tất cả user active trên plan đó bị áp dụng quota mới ngay trong lần upload kế tiếp.
- Nếu user đã dùng nhiều hơn quota mới sau khi admin giảm quota, hệ thống không xóa file tự động nhưng chặn mọi upload/create resource mới cho đến khi `used_bytes + reserved_bytes < quotaBytes`.
- User không có active subscription dùng free-tier storage limit từ `UserSubscriptionEntitlement`, và free-tier limit cũng phải được trả trong API usage để FE hiển thị.

### Free tier storage setting

Free users không có subscription plan nên quota mặc định phải nằm trong admin config, không nằm trong một subscription row ảo. Giá trị mặc định khi seed config là `104857600` bytes.

Admin API:

```http
GET /api/User/admin/storage/settings/free-tier
PUT /api/User/admin/storage/settings/free-tier
Content-Type: application/json

{
  "freeStorageQuotaBytes": 104857600
}
```

Response dùng `Result<StorageSettingsResponse>` và chỉ trả field thuộc storage:

```json
{
  "value": {
    "freeStorageQuotaBytes": 104857600,
    "freeStorageQuotaMb": 100,
    "updatedAt": "2026-04-24T05:37:55.330204Z"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

Rule:

- `freeStorageQuotaBytes` phải `>= 0`.
- `0` nghĩa là free users không được upload resource mới.
- User không có active subscription dùng ngay giá trị config mới trong lần upload kế tiếp.
- User free đang over quota sau khi admin giảm quota vẫn được xem/xóa resource cũ, nhưng upload mới bị chặn.

### System-wide storage setting

Admin phải cấu hình được tổng dung lượng storage toàn hệ thống. Setting này dùng để vận hành/cảnh báo tổng capacity, tách biệt với quota từng user/plan.

```http
GET /api/User/admin/storage/settings/system
PUT /api/User/admin/storage/settings/system
Content-Type: application/json

{
  "systemStorageQuotaBytes": 107374182400
}
```

`systemStorageQuotaBytes = null` nghĩa là chưa đặt giới hạn tổng hệ thống. Response luôn trả tổng đã dùng:

```json
{
  "value": {
    "systemStorageQuotaBytes": 107374182400,
    "systemStorageQuotaGb": 100,
    "usedBytes": 5368709120,
    "usedGb": 5,
    "availableBytes": 102005473280,
    "availableGb": 95,
    "usagePercent": 5,
    "resourceCount": 120,
    "userCount": 30,
    "updatedAt": "2026-04-24T05:37:55.330204Z"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

Rule:

- `systemStorageQuotaBytes` phải `>= 0` hoặc `null`.
- System quota không thay thế quota theo user/plan; upload phải pass cả user quota và system quota.
- Khi `usedBytes + requestedBytes > systemStorageQuotaBytes`, mọi flow tạo resource mới bị chặn trước khi ghi S3 với lỗi `Resource.SystemStorageQuotaExceeded`.

## Upload/quota flow

Áp dụng cho:

- User upload qua `POST /api/User/resources`.
- User update file qua `PUT /api/User/resources/{id}`.
- Avatar upload.
- Backend upload remote/generated media thay user.

Flow chuẩn:

1. Validate file: content type, content length, max file size.
2. Resolve current subscription entitlement.
3. Tính `quotaBytes`, `usedBytes`, `reservedBytes`, `availableBytes`.
4. Nếu `contentLength > max_upload_file_bytes`, trả lỗi `Resource.FileTooLarge`.
5. Nếu `usedBytes + reservedBytes + contentLength > quotaBytes`, trả lỗi `Resource.StorageQuotaExceeded`.
6. Reserve bytes trong PostgreSQL bằng transaction/row lock.
7. Upload S3 vào key `{namespace}/resources/{userId}/{resourceId}`.
8. Insert/update `resources` với `size_bytes`, `storage_key`, namespace, bucket, content type.
9. Commit usage: trừ reserved, cộng used.
10. Nếu S3 upload fail, release reservation và trả lỗi S3 hiện có.

Các command phải enforce cùng một rule:

- `UploadResourceFileCommand`.
- `UpdateResourceFileCommand`; nếu replace file thì quota delta = `newSize - oldSize` khi old object vẫn tính quota, hoặc `newSize` nếu object cũ vẫn được giữ theo retention policy.
- `UpdateAvatarCommand`.
- `UploadResourceFromUrlCommand`.
- Mọi flow AI/generated media nếu tạo `Resource` thay user.

Các flow chỉ đọc, presign, list, soft-delete resource không bị chặn bởi quota.

Response lỗi quota nên giữ shape `ProblemDetails` qua `HandleFailure(result)`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Storage quota exceeded.",
  "errors": {
    "code": "Resource.StorageQuotaExceeded",
    "quotaBytes": 10737418240,
    "usedBytes": 10485760000,
    "requestedBytes": 524288000,
    "availableBytes": 251658240
  }
}
```

## Cleanup/retention policy

Soft delete hiện tại giữ object để các post/product pages đã pin resource id vẫn còn xem được. FR-A7 không nên đổi hành vi này đột ngột.

Policy đề xuất:

- User delete resource: set `is_deleted=true`, `deleted_at=now`, không xóa S3 ngay.
- `expires_at = deleted_at + retention_days_after_delete`.
- Cleanup job chỉ hard-delete S3 khi:
  - `is_deleted=true`;
  - `expires_at <= now`;
  - không còn reference bắt buộc giữ lại từ post-builder/feed/product page hoặc policy cho phép xóa orphan only;
  - object thuộc namespace hiện tại.
- Sau hard delete: set `deleted_from_storage_at=now`; resource row vẫn giữ để audit.

Admin policy scopes:

- Global default policy.
- Plan-level override: lấy từ `SubscriptionLimits.retention_days_after_delete`.
- User-level override nếu cần hỗ trợ khách hàng cụ thể.
- Namespace cleanup policy chỉ dành cho local/dev, không dùng cho production customer data nếu chưa có xác nhận.

## Admin API contract đề xuất

Tất cả route đi qua API Gateway bằng prefix `/api/User`, dùng admin auth giống các admin controller hiện có.

### Quản lý storage quota theo subscription plan

Admin phải có API đầy đủ để xem và chỉnh storage limit của từng gói. Route chính nên bám controller hiện có:

```http
GET /api/User/admin/subscriptions
GET /api/User/admin/subscriptions/{subscriptionId}
POST /api/User/admin/subscriptions
PUT /api/User/admin/subscriptions/{subscriptionId}
PATCH /api/User/admin/subscriptions/{subscriptionId}
```

Các request/response của subscription phải bao gồm storage fields trong `limits`:

```json
{
  "id": "guid",
  "name": "Subscription 10000",
  "cost": 100000,
  "durationMonths": 1,
  "meAiCoin": 10000,
  "isActive": true,
  "limits": {
    "number_of_social_accounts": 8,
    "rate_limit_for_content_creation": 5,
    "number_of_workspaces": null,
    "max_pages_per_social_account": 10,
    "storage_quota_bytes": 10737418240,
    "max_upload_file_bytes": 524288000,
    "retention_days_after_delete": 30
  }
}
```

Tạo gói mới có giới hạn storage:

```http
POST /api/User/admin/subscriptions
Content-Type: application/json

{
  "name": "Creator 10GB",
  "cost": 199000,
  "durationMonths": 1,
  "meAiCoin": 10000,
  "limits": {
    "number_of_social_accounts": 8,
    "rate_limit_for_content_creation": 5,
    "number_of_workspaces": 3,
    "max_pages_per_social_account": 10,
    "storage_quota_bytes": 10737418240,
    "max_upload_file_bytes": 524288000,
    "retention_days_after_delete": 30
  }
}
```

Cập nhật riêng storage policy của gói:

```http
PATCH /api/User/admin/subscriptions/{subscriptionId}
Content-Type: application/json

{
  "limits": {
    "storage_quota_bytes": 21474836480,
    "max_upload_file_bytes": 1073741824,
    "retention_days_after_delete": 45
  }
}
```

Response vẫn là `Result<Subscription>` để giữ contract hiện có:

```json
{
  "value": {
    "id": "guid",
    "name": "Creator 20GB",
    "limits": {
      "storage_quota_bytes": 21474836480,
      "max_upload_file_bytes": 1073741824,
      "retention_days_after_delete": 45
    }
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

Validation:

- `storage_quota_bytes` phải `>= 0` nếu có giá trị.
- `max_upload_file_bytes` phải `> 0` nếu có giá trị.
- `max_upload_file_bytes` không được lớn hơn `storage_quota_bytes` khi quota không null và quota lớn hơn 0.
- `retention_days_after_delete` phải `>= 0`; `0` nghĩa là eligible cleanup ngay sau soft delete nếu không còn reference.
- Khi update `limits` bằng `PATCH`, backend merge partial fields vào JSONB hiện tại, không reset các limit khác về null.

Nếu FE muốn màn hình storage policy riêng, có thể thêm alias endpoint mỏng gọi cùng command subscription để tránh duplicate logic:

```http
GET /api/User/admin/storage/plans
GET /api/User/admin/storage/plans/{subscriptionId}
PATCH /api/User/admin/storage/plans/{subscriptionId}
```

Alias response nên trả DTO tập trung vào storage để FE dễ render:

```json
{
  "value": {
    "subscriptionId": "guid",
    "subscriptionName": "Creator 20GB",
    "isActive": true,
    "storageQuotaBytes": 21474836480,
    "maxUploadFileBytes": 1073741824,
    "retentionDaysAfterDelete": 45,
    "activeUserCount": 128,
    "usersOverQuotaCount": 3
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

### Xem usage tổng quan

```http
GET /api/User/admin/storage/usage?userId={guid?}&namespace={text?}
```

Admin có route detail rõ ràng cho từng user:

```http
GET /api/User/admin/storage/usage/users/{userId}
```

Nên hỗ trợ filter theo plan để admin kiểm tra các user bị ảnh hưởng khi đổi quota:

```http
GET /api/User/admin/storage/usage?subscriptionId={guid?}&overQuotaOnly=true&namespace={text?}
```

Response:

```json
{
  "value": {
    "namespace": "khanghv-local",
    "totalUsedBytes": 123456789,
    "totalReservedBytes": 0,
    "totalResourceCount": 42,
    "users": [
      {
        "userId": "guid",
        "email": "user@example.com",
        "planId": "guid",
        "planName": "Subscription 10000",
        "quotaBytes": 10737418240,
        "usedBytes": 5368709120,
        "reservedBytes": 0,
        "availableBytes": 5368709120,
        "usagePercent": 50.0,
        "isOverQuota": false,
        "resourceCount": 18
      }
    ]
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

### Xem resources để admin quản lý

```http
GET /api/User/admin/storage/resources?userId={guid?}&workspaceId={guid?}&resourceType=image|video&includeDeleted=true&namespace={text?}&cursorCreatedAt={iso?}&cursorId={guid?}&limit=50
```

Mỗi item nên có:

- `id`, `userId`, `workspaceId`.
- `resourceType`, `contentType`, `sizeBytes`.
- `storageBucket`, `storageRegion`, `storageNamespace`, `storageKey`.
- `createdAt`, `updatedAt`, `deletedAt`, `expiresAt`, `deletedFromStorageAt`.
- `presignedUrl` chỉ trả khi admin yêu cầu `includePresignedUrl=true` để tránh tạo presign hàng loạt không cần thiết.

### Cập nhật quota/retention của plan qua subscription API

Route subscription admin hiện có là API canonical để admin chỉnh storage của gói:

```http
PATCH /api/User/admin/subscriptions/{subscriptionId}
Content-Type: application/json

{
  "limits": {
    "storage_quota_bytes": 10737418240,
    "max_upload_file_bytes": 524288000,
    "retention_days_after_delete": 30
  }
}
```

Frontend admin chỉ cần gửi partial `limits`; backend merge vào JSONB như các limit hiện có.

Sau khi update quota của plan:

- Backend không cần update từng user.
- API usage phải phản ánh quota mới ngay.
- User đang over quota vẫn xem/download/delete resource bình thường, nhưng upload mới trả `Resource.StorageQuotaExceeded`.
- Admin storage usage page nên có filter `overQuotaOnly=true` để tìm user cần cleanup hoặc nâng gói.

### Cleanup manual

```http
POST /api/User/admin/storage/cleanup/run
Content-Type: application/json

{
  "dryRun": true,
  "deleteExpiredResources": true,
  "deleteOrphanObjects": false,
  "olderThanDays": 30,
  "namespace": null
}
```

Behavior:

- Luôn scope theo namespace hiện tại của service.
- Nếu request không truyền `namespace` hoặc truyền `null`, backend dùng `S3__Namespace` trong env.
- Nếu request truyền `namespace` khác `S3__Namespace` trong env, backend trả lỗi `Storage.NamespaceMismatch` và không scan/xóa gì.
- `dryRun` mặc định là `true`; nếu không truyền gì thì chỉ đếm candidate, không xóa.
- `deleteExpiredResources=true`: hard-delete S3 object của resource đã soft-delete quá `olderThanDays`.
- `deleteOrphanObjects=true`: xóa S3 objects trong namespace hiện tại nhưng không còn row tương ứng trong PostgreSQL.
- Khi hard-delete resource object thành công, backend set `resources.deleted_from_storage_at` và `resources.last_verified_at`.

Dry-run response:

```json
{
  "value": {
    "dryRun": true,
    "namespace": "khanghv-local",
    "expiredResourceCandidateCount": 12,
    "expiredResourceCandidateBytes": 734003200,
    "expiredResourceDeletedCount": 0,
    "expiredResourceDeletedBytes": 0,
    "orphanObjectCandidateCount": 3,
    "orphanObjectCandidateBytes": 10485760,
    "orphanObjectDeletedCount": 0,
    "orphanObjectDeletedBytes": 0,
    "errors": []
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

### Reconciliation sau khi reset PostgreSQL local

```http
POST /api/User/admin/storage/reconcile
Content-Type: application/json

{
  "dryRun": true,
  "markMissingObjects": false,
  "namespace": null
}
```

Use case:

- `dryRun=true`: scan DB resources và S3 objects trong namespace hiện tại, báo object nào lệch.
- Nếu request không truyền `namespace` hoặc truyền `null`, backend dùng `S3__Namespace` trong env.
- Nếu request truyền `namespace` khác `S3__Namespace` trong env, backend trả lỗi `Storage.NamespaceMismatch`.
- `markMissingObjects=true` + `dryRun=false`: DB resources có key nhưng S3 object đã mất sẽ được set `deleted_from_storage_at`, `last_verified_at`, và status `storage_missing` nếu chưa có status.
- Orphan S3 objects chỉ được report bởi reconcile. Muốn xóa orphan phải gọi `POST /api/User/admin/storage/cleanup/run` với `deleteOrphanObjects=true`.

Response:

```json
{
  "value": {
    "dryRun": true,
    "namespace": "khanghv-local",
    "databaseResourceCount": 80,
    "storageObjectCount": 90,
    "missingObjectCount": 2,
    "markedMissingCount": 0,
    "verifiedResourceCount": 0,
    "orphanObjectCount": 12,
    "orphanObjectBytes": 52428800,
    "missingResourceIds": ["resource-guid"],
    "orphanObjectKeys": ["khanghv-local/resources/..."],
    "errors": []
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

Không tự động import orphan vào production vì S3 object không đủ thông tin business để khôi phục user/subscription chính xác nếu key không chuẩn. Với local/dev, nếu PostgreSQL bị reset volume, cách sạch nhất là chạy cleanup dry-run trước, sau đó chạy cleanup orphan thật trong namespace của dev đó.

### Namespace rules for cleanup/reconcile

Cleanup/reconcile là API nguy hiểm vì có thể xóa object thật trên S3. Rule bắt buộc:

- Namespace mặc định luôn lấy từ env `S3__Namespace`.
- Frontend/admin không cần gửi namespace trong request thông thường.
- Nếu frontend/admin gửi `namespace`, giá trị đó chỉ hợp lệ khi bằng đúng `S3__Namespace` của service đang chạy.
- Backend không hỗ trợ cleanup namespace khác env trong endpoint này.
- Khi team dùng chung bucket S3, mỗi dev phải chạy service với namespace riêng, ví dụ `MEAI_S3_NAMESPACE=khanghv-local`.
- Nếu local PostgreSQL bị reset volume, chỉ cleanup orphan sau khi đã chạy dry-run và kiểm tra response `orphanObjectKeys`.

Ví dụ dry-run an toàn sau khi reset DB local:

```http
POST /api/User/admin/storage/reconcile
Content-Type: application/json

{
  "dryRun": true,
  "markMissingObjects": false
}
```

Nếu response cho thấy orphan đúng là object của namespace dev hiện tại, chạy cleanup thật:

```http
POST /api/User/admin/storage/cleanup/run
Content-Type: application/json

{
  "dryRun": false,
  "deleteExpiredResources": false,
  "deleteOrphanObjects": true,
  "olderThanDays": 0
}
```

## Frontend behavior

Admin storage page nên có 3 tab:

- Usage by user: sort theo usage percent, filter plan/namespace, show used/quota/available.
- Resources: filter user/type/status/deleted, xem metadata và presigned preview khi cần.
- Plans/Policies: tạo/sửa subscription storage quota, max upload size, retention days, xem số user active và số user đang over quota.
- Cleanup: chạy dry-run cleanup và reconcile namespace.

FE phải coi `quotaBytes=null` là unlimited. Khi `quotaBytes=0`, hiển thị "Uploads disabled".

User upload UI nên đọc quota trước khi upload:

```http
GET /api/User/resources/storage-usage
```

Response đề xuất:

```json
{
  "value": {
    "quotaBytes": 10737418240,
    "usedBytes": 5368709120,
    "reservedBytes": 0,
    "availableBytes": 5368709120,
    "usagePercent": 50.0,
    "maxUploadFileBytes": 524288000
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

FE vẫn phải xử lý backend quota error vì usage có thể thay đổi giữa lúc user chọn file và lúc upload thật.

## Implementation plan

1. Add `S3__Namespace` config and validate it in `S3ObjectStorageService` or a dedicated `S3StorageOptions`.
2. Change `ResourceStorageKey.Build` to include namespace, while keeping existing legacy keys readable.
3. Add migration for new `resources` columns and `user_storage_usages`.
4. Extend `StorageUploadResult` to include `Bucket`, `Region`, `Namespace`, `Key`, `ETag`, `SizeBytes` if available.
5. Extend `SubscriptionLimits` with storage quota fields and validators.
6. Add `IStorageUsageService` to resolve entitlement, reserve/release bytes, rebuild usage from resources, and return usage DTOs.
7. Update admin subscription create/update/patch DTOs, validators and OpenAPI so storage fields are visible in Scalar.
8. Add optional storage-plan alias endpoints if FE wants a dedicated storage policy screen.
9. Enforce quota in all resource upload/update/avatar/remote upload command handlers.
10. Add admin storage usage/resources controller routes and OpenAPI annotations.
11. Add cleanup/reconciliation service with dry-run first. Implemented endpoints:
    - `POST /api/User/admin/storage/cleanup/run`
    - `POST /api/User/admin/storage/reconcile`
12. Update compose dev/production env with `S3__Namespace`.
13. Add tests for plan storage update, quota exceeded, user over-quota after plan downgrade, reservation release on S3 failure, namespace key generation, cleanup dry-run and subscription limit serialization.

## Acceptance criteria

- Admin can view used storage per user and compare it with that user's current plan quota.
- Admin can create/update/patch subscription plans with `storage_quota_bytes`, `max_upload_file_bytes`, and `retention_days_after_delete`.
- Users with a storage-limited subscription receive exactly that plan quota at runtime.
- Upload is rejected before S3 write when known file size exceeds available quota.
- User over quota after a plan downgrade cannot upload/create any new resource, but can still list, preview, download, and delete existing resources.
- Concurrent uploads cannot exceed quota through race conditions.
- S3 keys are always namespaced per dev/environment.
- Resetting local PostgreSQL does not affect other developers' S3 objects because namespaces are isolated.
- Cleanup can run dry-run and real mode, and never deletes outside the configured namespace.
- Subscription plan API accepts and returns storage quota fields without breaking existing FE response shape.
- Admin has an API to list over-quota users by subscription plan.
- Existing resource APIs keep their current `Result<T>` success envelope and `ProblemDetails` failure shape.
