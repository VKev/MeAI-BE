# Storage Quota On Generate

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của pre-check storage quota trước khi AI generate resource mới.

### API và RPC đã triển khai

- [x] gRPC `UserResourceService.CheckStorageQuota`
- [x] AI service gọi quota check qua `IUserResourceService.CheckStorageQuotaAsync(...)`

### Phạm vi hiện tại

- [x] Áp dụng cho image generation.
- [x] Áp dụng cho video generation.
- [x] Không áp dụng cho caption-only flow.

## Pre-check flow

AI generation sẽ estimate dung lượng cần dùng trước khi submit job.

Nếu estimate vượt quota:

- dừng ngay
- chưa debit coin
- chưa persist chat
- chưa publish bus message

## Request/response của quota RPC

Request:

- `user_id`
- `requested_bytes`
- `purpose`
- `estimated_file_count`
- `workspace_id`

Response:

- `allowed`
- `quota_bytes`
- `used_bytes`
- `reserved_bytes`
- `available_bytes`
- `max_upload_file_bytes`
- `system_storage_quota_bytes`
- `error_code`
- `error_message`

## Estimate policy

### Image

- `1K` = `5 MB`
- `2K` = `12 MB`

### Video

- `veo3_fast` = `150 MB`
- `veo3` = `250 MB`
- `veo3_quality` = `350 MB`

## Khi quota không đủ

Error code hiện có:

- `Resource.StorageQuotaExceeded`
- `Resource.SystemStorageQuotaExceeded`

Metadata hiện được enrich thêm:

- `quotaBytes`
- `usedBytes`
- `reservedBytes`
- `requestedBytes`
- `availableBytes`
- `estimatedBytes`
- `estimatedFileCount`
- `systemStorageQuotaBytes`

## Hạn chế hiện tại

- Chưa có reservation table thật.
- `reservedBytes` vẫn là `0`.
- Hai job đồng thời vẫn có thể cùng pass pre-check rồi fail ở bước upload thực tế.
- Upload-time quota enforcement trong User service vẫn giữ nguyên.
