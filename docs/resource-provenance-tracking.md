# FR-AI Resource Provenance Tracking

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại sau khi bổ sung provenance cho `resources` để phân biệt resource do user upload và resource do AI tạo/import.

### Phạm vi đã triển khai

- [x] Phân biệt `user_upload`, `ai_generated`, `ai_imported_url` ngay trên bảng `resources`.
- [x] Lưu được `origin_chat_session_id` và `origin_chat_id` cho resource do AI sở hữu.
- [x] Giữ được `origin_source_url` cho resource được tạo từ URL mới import.
- [x] Expose provenance qua `User.Microservice` resource responses.
- [x] Expose provenance qua `Ai.Microservice` AI resource listing responses.
- [x] Filter được resource theo `originKind` qua query param `originKinds`.
- [x] Propagate provenance qua gRPC `user_resources.proto`.
- [x] Gắn provenance cho 2 luồng AI chính:
  - web/direct-url import
  - image/video generation callback import
- [x] Backfill lịch sử theo kiểu best-effort từ `Ai.Chat`.
- [x] Ghi note triển khai tập trung trong file này.

### API và contract đã bị ảnh hưởng

#### User API

- [x] `GET /api/User/resources`
- [x] `GET /api/User/resources/workspace/{workspaceId}`
- [x] `GET /api/User/resources/{id}`
- [x] `GET /api/User/admin/storage/resources`

Các endpoint trên vẫn giữ nguyên envelope `Result<T>`, nhưng mỗi `ResourceResponse` giờ có thêm:

- `originKind`
- `originSourceUrl`
- `originChatSessionId`
- `originChatId`

Các endpoint list giờ hỗ trợ query param tùy chọn:

- `originKinds=user_upload`
- `originKinds=ai_generated`
- `originKinds=ai_imported_url`
- hoặc truyền nhiều giá trị `originKinds`

#### AI API

- [x] `GET /api/Ai/chats/resources`
- [x] `GET /api/Ai/chats/workspace/{workspaceId}/resources`

`WorkspaceAiResourceResponse` giờ cũng trả thêm các field provenance tương ứng.

#### gRPC

- [x] `GetPresignedResources`
- [x] `GetPublicResources`
- [x] `CreateResourcesFromUrls`
- [x] `BackfillResourceProvenance` mới

`PresignedResource` và `CreatedResource` giờ trả thêm:

- `origin_kind`
- `origin_source_url`
- `origin_chat_session_id`
- `origin_chat_id`

### Đã có trong code nhưng còn hạn chế

- Backfill lịch sử chỉ best-effort:
  - `ai_generated` lấy từ `Chat.ResultResourceIds`
  - `ai_imported_url` lấy từ `AgentChatMetadata.ImportedResourceIds`
- Với dữ liệu lịch sử, `origin_source_url` có thể không khôi phục lại được vì chat cũ không luôn lưu raw source URL theo từng imported resource.
- Image/video callback hiện vẫn tìm chat theo `correlationId` bằng cách scan `Chat.Config` rồi `Contains(...)`; provenance đã được gắn đúng sau khi match được chat, nhưng lookup này chưa phải thiết kế tối ưu dài hạn.

### Chưa triển khai theo hướng dài hạn

- [ ] Index/search/reporting chuyên biệt theo provenance cho admin dashboard.
- [ ] API filter chính thức theo `originChatSessionId`, `originChatId`.
- [ ] Persist đầy đủ source-url mapping cho backfill lịch sử 100% chính xác.
- [ ] Correlation-to-chat mapping trực tiếp thay vì scan `Chat.Config`.
- [ ] Provenance audit trail nhiều lớp nếu sau này một resource có lifecycle phức tạp hơn một origin duy nhất.

## Mục tiêu

Feature này nhằm giải quyết 3 yêu cầu nghiệp vụ:

- Phân biệt được resource do user upload với resource do AI tạo ra hoặc AI kéo từ URL.
- Với resource do AI sở hữu, biết được resource đó thuộc `chat session` nào.
- Với resource do AI sở hữu, biết được `message` nào đã sinh ra resource đó.

Backend hiện dùng `User.Microservice` làm nơi lưu metadata `Resource`, còn `Ai.Microservice` giữ `ChatSession` và `Chat`. Vì vậy provenance phải được thiết kế xuyên service nhưng vẫn đủ đơn giản để query nhanh.

## Quyết định thiết kế

### Có nên lưu provenance trực tiếp trên bảng `resources` không?

Có.

Thay vì chỉ suy ngược từ `Ai.Chat` hoặc tạo thêm một bảng provenance riêng, implementation hiện tại lưu trực tiếp provenance trên `resources`.

Lý do:

- `Resource` là source of truth cho FE khi render thư viện media của user.
- FE cần biết ngay resource là của user upload hay AI tạo/import mà không phải gọi chéo sang AI service.
- Các query như “lấy resource AI của workspace” hoặc “resource này do message nào tạo” sẽ đơn giản hơn nhiều nếu metadata nằm ngay trên row resource.
- Mỗi resource trong phạm vi hiện tại chỉ có một origin gốc, nên thêm cột trực tiếp là đủ.

### Message identity nào được dùng?

Dùng `Ai.Chat.Id`.

Quy ước hiện tại:

- `ChatSession` đại diện cho conversation container.
- `Chat` đại diện cho từng message business-level đã được persist.
- Với agent flow, assistant message cũng được persist vào `Chat`, nên `Chat.Id` có thể dùng thống nhất như message id.

Điều này tránh phải tạo thêm bảng message identity riêng.

### Origin taxonomy nào đang dùng?

Hiện tại dùng đúng 3 giá trị:

- `user_upload`
- `ai_generated`
- `ai_imported_url`

Ý nghĩa:

- `user_upload`: file do user upload hoặc create resource theo flow user-side.
- `ai_generated`: media result do AI generation callback import về storage của hệ thống.
- `ai_imported_url`: media được AI lấy từ external URL rồi import thành resource của user.

### Khi replace file của một resource thì provenance có đổi không?

Không.

Implementation giữ nguyên provenance nếu `resourceId` không đổi.

Lý do:

- `resourceId` đang là identity business của resource.
- Replace file hiện được coi là cập nhật nội dung file của cùng một resource, không phải sinh lineage mới.
- Nếu sau này cần lineage chi tiết kiểu versioning, phải có model riêng cho resource revision thay vì mutate logic provenance hiện tại.

## Data model hiện tại

### Bảng `resources`

Đã được mở rộng thêm các cột nullable:

| Column | Type | Ý nghĩa |
|---|---:|---|
| `origin_kind` | text | loại origin: `user_upload`, `ai_generated`, `ai_imported_url` |
| `origin_source_url` | text nullable | URL nguồn ban đầu nếu resource được import từ URL hoặc từ callback URL |
| `origin_chat_session_id` | uuid nullable | session AI liên quan |
| `origin_chat_id` | uuid nullable | message AI liên quan |

Các cột cũ như `user_id`, `workspace_id`, `storage_namespace`, `storage_key`, `size_bytes` vẫn giữ nguyên vai trò.

### Index mới

Đã thêm:

- `ix_resources_user_origin_session` trên `(user_id, origin_kind, origin_chat_session_id)`

Mục tiêu:

- hỗ trợ query nhanh resource provenance theo user/session
- tránh phải luôn suy ngược từ AI DB khi chỉ cần filter ở User side

## Luồng nghiệp vụ hiện tại

### 1. User upload file

Luồng:

- `POST /api/User/resources`
- `UploadResourceFileCommand`
- insert `Resource`

Kết quả provenance:

- `origin_kind = user_upload`
- `origin_source_url = null`
- `origin_chat_session_id = null`
- `origin_chat_id = null`

### 2. AI import media từ URL

Áp dụng cho 2 kiểu:

- prompt chứa `direct_url`
- prompt cần `web_search`, rồi enrichment/import media

Luồng:

- `AgentSessionsController.SendMessage`
- `SendAgentMessageCommand`
- cấp trước một `assistantChatId`
- `GeminiAgentChatService`
- `ChatWebPostService`
- `WebSearchEnrichmentService`
- `UserResourceGrpcService.CreateResourcesFromUrlsAsync`
- `User.Microservice` tạo `Resource`

Kết quả provenance:

- `origin_kind = ai_imported_url`
- `origin_chat_session_id = session hiện tại`
- `origin_chat_id = assistantChatId`
- `origin_source_url = url media được import`

Quyết định quan trọng ở đây:

- assistant message id phải được tạo trước khi import resource
- nếu không, imported resource sẽ không thể gắn đúng `messageId`

### 3. AI image generation callback

Luồng:

- `CreateChatImageCommand` tạo `Chat`
- provider callback đi vào `ImageCompletedConsumer`
- consumer import `resultUrls` về User resources
- sau đó cập nhật `Chat.ResultResourceIds`

Kết quả provenance:

- `origin_kind = ai_generated`
- `origin_chat_session_id = chat.SessionId`
- `origin_chat_id = chat.Id`
- `origin_source_url = callback result URL`

### 4. AI video generation callback

Luồng tương tự image:

- `CreateChatVideoCommand`
- `VideoCompletedConsumer`
- import result URLs thành resources
- update `Chat.ResultResourceIds`

Kết quả provenance:

- `origin_kind = ai_generated`
- `origin_chat_session_id = chat.SessionId`
- `origin_chat_id = chat.Id`
- `origin_source_url = callback result URL`

## Ảnh hưởng lên API

### User resource responses

Các response resource của `User.Microservice` vẫn giữ contract envelope cũ, nhưng item giờ có thêm provenance fields.

Hiện tại các query sau đã trả provenance:

- `GetResourcesQuery`
- `GetWorkspaceResourcesQuery`
- `GetResourceByIdQuery`
- `GetResourcesByIdsQuery`
- `GetPublicResourcesQuery`
- `GetAdminStorageResourcesQuery`

Các endpoint list hiện support filter:

```http
GET /api/User/resources?originKinds=user_upload
GET /api/User/resources?originKinds=ai_generated&originKinds=ai_imported_url
GET /api/User/resources/workspace/{workspaceId}?originKinds=ai_generated
GET /api/User/admin/storage/resources?originKinds=ai_imported_url
```

### AI workspace resources

`GET /api/Ai/chats/workspace/{workspaceId}/resources` hiện hoạt động như sau:

- lấy các `ChatSession` thuộc `userId + workspaceId`
- lấy tất cả `Chat` trong các session đó
- parse `ResultResourceIds`
- gọi User service để lấy presigned URL + provenance
- filter theo `resourceTypes` nếu có

Response mỗi item gồm:

- `chatSessionId`
- `chatId`
- `resourceId`
- `presignedUrl`
- `contentType`
- `resourceType`
- `originKind`
- `originSourceUrl`
- `originChatSessionId`
- `originChatId`
- `chatCreatedAt`

API AI list resources hiện cũng support:

```http
GET /api/Ai/chats/resources?originKinds=ai_generated
GET /api/Ai/chats/resources?resourceTypes=image&originKinds=ai_imported_url
GET /api/Ai/chats/workspace/{workspaceId}/resources?originKinds=ai_generated
```

Điểm quan trọng:

- API này chỉ trả AI-generated resources lấy từ `Chat.ResultResourceIds`
- không phải toàn bộ library resources của workspace

### User workspace resources

`GET /api/User/resources/workspace/{workspaceId}`:

- chỉ query từ bảng `resources`
- filter:
  - `user_id = current user`
  - `workspace_id = workspaceId`
  - `is_deleted = false`
- dùng cursor pagination theo `created_at + id`

Nó là API “thư viện resource của user trong workspace”, không phải API “AI outputs của workspace”.

## Backfill dữ liệu lịch sử

### Vì sao không làm migration SQL thuần?

Không làm được đầy đủ bằng một migration SQL đơn lẻ vì:

- `resources` nằm ở `User.Microservice` database
- `chats` nằm ở `Ai.Microservice` database
- provenance lịch sử cần đọc `Ai.Chat` rồi gọi ngược sang User service để patch resource rows

Vì vậy implementation hiện tại dùng startup-safe backfill từ `Ai.Microservice`.

### Cách backfill đang chạy

`Ai.Microservice` startup:

- seed sample data như cũ
- sau đó chạy `ResourceProvenanceBackfillService`

Service này:

- đọc toàn bộ `Chat` chưa deleted
- sinh candidate:
  - `Chat.ResultResourceIds` => `ai_generated`
  - `AgentChatMetadata.ImportedResourceIds` => `ai_imported_url`
- group theo `resourceId`
- ưu tiên `ai_generated` nếu một resource xuất hiện ở nhiều nguồn
- gọi gRPC `BackfillResourceProvenance`
- User service chỉ fill vào row nào đang còn null provenance

### Giới hạn của backfill

- Không gán bừa `user_upload` cho dữ liệu cũ không rõ nguồn.
- Không luôn khôi phục được `origin_source_url` cho resource import cũ.
- Nếu chat history cũ không chứa imported ids hoặc result ids đúng, resource đó sẽ vẫn có provenance null.

## File và vùng code chính đã thay đổi

### SharedLibrary

- `SharedLibrary/Common/Resources/ResourceOriginKinds.cs`
- `SharedLibrary/Common/Resources/ResourceProvenanceMetadata.cs`
- `SharedLibrary/Protos/user_resources.proto`

### User.Microservice

- `Domain/Entities/Resource.cs`
- `Infrastructure/Context/Configuration/ResourceConfiguration.cs`
- `Infrastructure/Migrations/20260430090000_AddResourceProvenance.cs`
- `Application/Resources/Models/*`
- `Application/Resources/Queries/*`
- `Application/Resources/Commands/UploadResourceFileCommand.cs`
- `Application/Resources/Commands/UploadResourceFromUrlCommand.cs`
- `Application/Resources/Commands/BackfillResourceProvenanceCommand.cs`
- `WebApi/Grpc/UserResourceGrpcService.cs`

### Ai.Microservice

- `Application/Abstractions/Resources/IUserResourceService.cs`
- `Application/Abstractions/Automation/IWebSearchEnrichmentService.cs`
- `Application/Abstractions/Automation/IN8nWorkflowClient.cs`
- `Application/Abstractions/Agents/IAgentChatService.cs`
- `Application/Abstractions/Agents/IChatWebPostService.cs`
- `Application/Agents/Commands/SendAgentMessageCommand.cs`
- `Infrastructure/Logic/Agents/ChatWebPostService.cs`
- `Infrastructure/Logic/Agents/GeminiAgentChatService.cs`
- `Infrastructure/Logic/Automation/WebSearchEnrichmentService.cs`
- `Infrastructure/Logic/Automation/N8nWorkflowClient.cs`
- `Infrastructure/Logic/Consumers/ImageStatusConsumers.cs`
- `Infrastructure/Logic/Consumers/VideoStatusConsumers.cs`
- `Infrastructure/Logic/Services/ResourceProvenanceBackfillService.cs`
- `WebApi/Setups/ResourceProvenanceBackfillSetup.cs`
- `Application/Chats/Models/WorkspaceAiResourceResponse.cs`

## Test và validation

### Đã cập nhật test

Đã sửa các test bị ảnh hưởng bởi chữ ký method / request model:

- `ChatWebPostServiceTests`
- `HandleAgentScheduleRuntimeResultCommandTests`
- `SendAgentMessageCommandTests`

### Validation đã chạy

- `git diff --check` pass

### Validation chưa chốt được trong sandbox

Tôi chưa có tín hiệu compile cuối cùng đáng tin cậy từ môi trường sandbox cho:

- `dotnet build`
- `dotnet ef migrations add`

Lý do:

- local build phase trong sandbox đang fail ở lớp NuGet audit / restore behavior trước khi surfacing C# diagnostics hữu ích
- có case `Build FAILED` nhưng `0 Error(s)` nên không thể dùng làm tín hiệu xác nhận compile thật

Vì vậy trước khi merge thực sự, nên chạy lại local:

```bash
dotnet build Backend/Microservices/User.Microservice/src/WebApi/WebApi.csproj
dotnet build Backend/Microservices/Ai.Microservice/src/WebApi/WebApi.csproj
dotnet test Backend/Microservices/Ai.Microservice/test
```

## Open points và hướng cải tiến

### 1. Correlation lookup hiện chưa tối ưu

Image/video callback hiện vẫn match chat bằng:

- load candidate chats
- `chat.Config.Contains(correlationId)`

Điều này hoạt động được nhưng chưa tốt cho scale.

Hướng dài hạn:

- lưu `correlationId -> chatId` trực tiếp trong DB
- hoặc thêm cột structured vào `Chat`

### 2. API filter theo provenance chưa có

Hiện backend đã support filter theo `originKinds`, nhưng chưa support query sâu hơn như:

- chỉ lấy resource của `originChatSessionId = X`
- chỉ lấy resource của `originChatId = Y`

Nếu FE/admin cần drill-down theo session/message thì nên thêm query params riêng ở bước tiếp theo thay vì để FE filter toàn bộ client-side.

### 3. Historical imported source URL chưa đầy đủ

Với resource import mới, `origin_source_url` có đầy đủ.

Với dữ liệu cũ:

- nếu imported ids tồn tại nhưng source URL không còn được persist theo mapping resource-id, backfill sẽ không thể khôi phục URL chính xác

Nếu product muốn audit mạnh hơn, cần persist source mapping ngay trong chat metadata hoặc bảng riêng.

## Work log ngắn

- Xác định 3 đường tạo resource chính:
  - user upload
  - AI URL import
  - AI generation callback import
- Chốt `Ai.Chat.Id` là message id canonical.
- Chốt provenance nằm trực tiếp trên `resources`.
- Bổ sung gRPC để provenance đi xuyên service và có thể backfill lịch sử.
- Vá agent flow để assistant chat id tồn tại trước khi import resource.
- Vá image/video callback flow để provenance được đóng dấu ngay lúc tạo resource.
- Thêm startup backfill ở AI service cho dữ liệu lịch sử.
