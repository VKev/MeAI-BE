# AI spending time

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của timing enrichment cho lịch sử usage AI trong `Ai.Microservice`.

### API đã được mở rộng

- [x] `GET /api/Ai/usage/history`
- [x] `GET /api/Ai/admin/spending/ai/history`

### Field timing đã thêm vào response

- [x] `startedAtUtc`
- [x] `completedAtUtc`
- [x] `processingDurationSeconds`

### Phạm vi timing hiện tại

- [x] Image generation có timing enrichment.
- [x] Video generation có timing enrichment.
- [x] Caption generation trả `null` cho cả 3 field timing.

## Cách resolve timing

### Image generation

Với record có `referenceType = "chat_image"`:

1. Parse `referenceId` thành `Chat.Id`.
2. Batch load `Chat`.
3. Đọc `Chat.Config` để lấy `correlationId`.
4. Batch load `ImageTask` theo correlation id.
5. Map:
   - `startedAtUtc = ImageTask.CreatedAt`
   - `completedAtUtc = ImageTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` khi `CompletedAt` tồn tại

### Video generation

Với record có `referenceType = "chat_video"`:

1. Parse `referenceId` thành `Chat.Id`.
2. Batch load `Chat`.
3. Đọc `Chat.Config` để lấy `correlationId`.
4. Batch load `VideoTask` theo correlation id.
5. Map:
   - `startedAtUtc = VideoTask.CreatedAt`
   - `completedAtUtc = VideoTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` khi `CompletedAt` tồn tại

## Quy tắc trả `null`

Ba field timing được trả `null` khi:

- reference type không hỗ trợ timing enrichment
- `referenceId` không parse được thành chat id
- chat không tồn tại
- `Chat.Config` không hợp lệ hoặc không đọc được correlation id
- task image/video không tìm thấy

## Hạn chế hiện tại

- Timing chỉ là read-side join, không được lưu ngược vào `AiSpendRecord`.
- Nếu config chat thay đổi sau này, timing join phụ thuộc vào dữ liệu hiện tại đang lưu.
- Caption generation chưa có nguồn timing chuẩn nên vẫn trả `null`.

## Hiệu năng

Enrichment được xử lý theo batch theo page dữ liệu để tránh N+1 query.
