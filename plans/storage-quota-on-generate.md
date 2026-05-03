# Storage Quota On Generate

## Summary

Feature này chặn generate AI ngay khi user bấm Generate nếu hệ thống ước lượng chắc chắn rằng output mới sẽ vượt storage quota.

Quyết định đã chốt:
- Chặn trước khi submit job.
- Không làm quota reservation ở v1.
- Vẫn giữ quota check hiện tại ở bước upload resource như một lớp bảo vệ thứ hai.

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- `docs/storage-quota-on-generate.md`

File docs này phải mô tả trạng thái thực tế sau triển khai:
- nguyên tắc pre-check trước generate
- gRPC/API nội bộ mới dùng để check quota
- estimate policy cho image/video
- error payload khi quota không đủ
- quan hệ giữa pre-check và upload-time enforcement
- giới hạn/hạn chế của v1 vì chưa có reservation

## Current State

Code đã có sẵn:
- `User.Microservice/src/Application/Resources/Services/StorageUsageService.cs`
- `User.Microservice/src/Application/Resources/Commands/UploadResourceFileCommand.cs`
- `User.Microservice/src/Application/Resources/Commands/UploadResourceFromUrlCommand.cs`
- `User.Microservice/src/WebApi/Grpc/UserResourceGrpcService.cs`
- `SharedLibrary/Protos/user_resources.proto`
- `Ai.Microservice/src/Application/Chats/Commands/CreateChatImageCommand.cs`
- `Ai.Microservice/src/Application/Chats/Commands/CreateChatVideoCommand.cs`
- `Ai.Microservice/src/Infrastructure/Logic/Consumers/ImageStatusConsumers.cs`
- `Ai.Microservice/src/Infrastructure/Logic/Consumers/VideoStatusConsumers.cs`

Hiện trạng xác nhận:
- Upload trực tiếp và upload-from-url đã gọi `EnsureUploadAllowedAsync`.
- AI image/video hiện debit coin trước, submit job trước, rồi chỉ đến callback mới upload resource vào User service.
- `CreateResourcesFromUrlsAsync` hiện chỉ enforce quota ở thời điểm lưu file, chưa có pre-check riêng cho generate.

## Public Interfaces

Mở rộng `user_resources.proto`:

```proto
rpc CheckStorageQuota (CheckStorageQuotaRequest) returns (CheckStorageQuotaResponse);

message CheckStorageQuotaRequest {
  string user_id = 1;
  int64 requested_bytes = 2;
  string purpose = 3;
  int32 estimated_file_count = 4;
  string workspace_id = 5;
}

message CheckStorageQuotaResponse {
  bool allowed = 1;
  int64 quota_bytes = 2;
  int64 used_bytes = 3;
  int64 reserved_bytes = 4;
  int64 available_bytes = 5;
  int64 max_upload_file_bytes = 6;
  int64 system_storage_quota_bytes = 7;
  string error_code = 8;
  string error_message = 9;
}
```

Mở rộng abstraction trong Ai service:
- `IUserResourceService.CheckStorageQuotaAsync(...)`

Không thêm public REST endpoint mới ở v1.

## Implementation Changes

### User service

Thêm mediator query/command nội bộ để expose logic đang có của `StorageUsageService.EnsureUploadAllowedAsync` qua gRPC `CheckStorageQuota`.

`purpose` chỉ dùng cho logging/audit ở v1, chưa thay đổi business rule.

### Ai service

Thêm `IAiGenerationStorageEstimator` với 2 hàm:
- `EstimateImageGenerationBytes(model, resolution, expectedResultCount)`
- `EstimateVideoGenerationBytes(model)`

Estimator phải dùng cấu hình nội bộ `GenerationStorageEstimates`, không hard-code trong handler.

Default config v1:
- image `1K = 5 MB`
- image `2K = 12 MB`
- video `veo3_fast = 150 MB`
- video `veo3 = 250 MB`
- video `veo3_quality = 350 MB`

Áp dụng pre-check tại:
- `CreateChatImageCommandHandler`
- `CreateChatVideoCommandHandler`

Flow mới:
1. Tính `expectedResultCount` như hiện tại.
2. Estimate `requestedBytes`.
3. Gọi `CheckStorageQuotaAsync`.
4. Nếu fail thì return business failure ngay, chưa debit coin, chưa persist chat, chưa publish bus message.
5. Nếu pass thì tiếp tục flow hiện tại.

Callback upload hiện tại vẫn giữ nguyên `CreateResourcesFromUrlsAsync` quota check để chống drift giữa estimate và output thực tế.

## Error Contract

Khi quota không đủ:
- reuse error code `Resource.StorageQuotaExceeded`
- giữ response shape `ProblemDetails` hiện tại
- thêm metadata:
  - `quotaBytes`
  - `usedBytes`
  - `reservedBytes`
  - `requestedBytes`
  - `availableBytes`
  - `estimatedBytes`
  - `estimatedFileCount`

Khi system quota không đủ:
- reuse `Resource.SystemStorageQuotaExceeded`

## Tests

Happy path:
- image generate 1 result, quota đủ, flow vẫn debit và submit như cũ.
- video generate với quota đủ, flow vẫn hoạt động như cũ.

Quota failure:
- image generate nhiều ratio làm `expectedResultCount` tăng và bị chặn trước debit.
- video generate bị chặn trước debit nếu estimate vượt quota.
- system storage quota bị chặn với error code hiện tại.

Defense in depth:
- pre-check pass nhưng callback upload fail vì output thực tế lớn hơn estimate vẫn phải fail ở User service.
- khi callback fail, logic refund coin hiện tại vẫn chạy như cũ.

## Assumptions

- V1 dùng estimate bảo thủ, không reserve quota.
- `reservedBytes` trong response vẫn là `0` vì hệ thống chưa có reservation table thật.
- Scope chỉ áp dụng cho AI generation tạo resource mới, không áp dụng cho caption-only flows.

## Checklist

- [x] DONE Extend `SharedLibrary/Protos/user_resources.proto` with `CheckStorageQuota` request/response and service RPC.
- [x] DONE Expose the storage quota check from User service through mediator + gRPC while reusing existing quota logic.
- [x] DONE Add AI-side `IUserResourceService.CheckStorageQuotaAsync(...)` and map the new gRPC contract.
- [x] DONE Add configurable `GenerationStorageEstimates` plus `IAiGenerationStorageEstimator` implementation for image/video estimates.
- [x] DONE Apply quota pre-check before coin debit/persist/publish in `CreateChatImageCommandHandler` and `CreateChatVideoCommandHandler`.
- [x] DONE Preserve existing error contracts and enrich quota failures with estimate metadata.
- [x] DONE Add AI generation quota tests for happy-path and pre-debit failure scenarios.
- [x] DONE Create `docs/storage-quota-on-generate.md` describing the delivered behavior and limitations.
- [x] DONE Validate with targeted tests and service builds.
