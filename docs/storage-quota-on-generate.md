# Storage Quota On Generate

## Pre-check principle

AI image/video generation now performs a storage quota pre-check before the job is submitted. The handler estimates the expected output size, calls the internal User service quota RPC, and stops immediately when the estimate would exceed either the user quota or the system quota.

This happens before any of these side effects:

- coin debit
- chat persistence
- spend-record persistence
- bus publish / generation submission

## Internal quota API

The feature adds an internal gRPC RPC on `UserResourceService`:

```proto
rpc CheckStorageQuota (CheckStorageQuotaRequest) returns (CheckStorageQuotaResponse);
```

Request fields:

- `user_id`
- `requested_bytes`
- `purpose`
- `estimated_file_count`
- `workspace_id`

Response fields:

- `allowed`
- `quota_bytes`
- `used_bytes`
- `reserved_bytes`
- `available_bytes`
- `max_upload_file_bytes`
- `system_storage_quota_bytes`
- `error_code`
- `error_message`

There is still no public REST endpoint for this in v1. AI service uses the new RPC through `IUserResourceService.CheckStorageQuotaAsync(...)`.

## Estimate policy

The AI service resolves estimates through `GenerationStorageEstimates` configuration instead of hard-coding values in handlers.

### Images

Per-result estimate by resolution:

- `1K` = `5 MB`
- `2K` = `12 MB`

Image requested bytes = `per-result estimate * expectedResultCount`

For image generation with social targets, `expectedResultCount` is:

- `1` source image
- plus each distinct extra target ratio that differs from the chosen source ratio

### Videos

Per-video estimate by model:

- `veo3_fast` = `150 MB`
- `veo3` = `250 MB`
- `veo3_quality` = `350 MB`

Video requested bytes is always the configured model estimate for a single output in v1.

## Error payload when quota is insufficient

The implementation preserves the existing business error codes and overall ProblemDetails flow.

### User quota exceeded

- error code: `Resource.StorageQuotaExceeded`

### System quota exceeded

- error code: `Resource.SystemStorageQuotaExceeded`

### Metadata attached by AI pre-check failures

When the pre-check fails, the returned error metadata includes:

- `quotaBytes`
- `usedBytes`
- `reservedBytes`
- `requestedBytes`
- `availableBytes`
- `estimatedBytes`
- `estimatedFileCount`

Additionally, system quota failures also include:

- `systemStorageQuotaBytes`

This keeps the response contract compatible while giving FE enough information to explain why generate was blocked.

## Relationship with upload-time enforcement

The pre-check is only the first barrier.

The existing upload-time quota enforcement remains active in User service when callback consumers later call `CreateResourcesFromUrlsAsync`. That means:

1. AI generation can be blocked early when the estimate is already too large.
2. If the estimate passes but the actual generated asset is larger than expected, User service can still reject the final upload.
3. Existing failure/refund behavior on callback-side upload failure stays in place.

This preserves defense in depth and avoids weakening existing quota enforcement.

## V1 limitations

This version intentionally does not reserve quota.

Known limitations:

- `reservedBytes` is still `0` because there is no reservation table yet.
- Two concurrent generations can both pass pre-check and still race with later uploads.
- Final upload can still fail if actual output size is larger than the estimate.
- `purpose` is carried through the gRPC contract for future logging/audit use, but it does not alter quota rules in v1.
- The feature applies only to AI generation flows that create new resources, not caption-only flows.
