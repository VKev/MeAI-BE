# PostBuilder Origin và Chuẩn hóa Draft Flow

## Trạng thái triển khai

Tài liệu này mô tả thay đổi backend đã được triển khai trong `Ai.Microservice` để:

- chuẩn hóa việc tạo draft post theo hướng mọi draft mới đều thuộc một `PostBuilder`;
- lưu được `origin` của `PostBuilder`;
- expose `origin` và `postBuilderId` ra response để FE phân biệt nguồn tạo draft.

### Phạm vi đã triển khai

- [x] `PostBuilder` có thêm `origin_kind`.
- [x] Định nghĩa 3 origin chính thức cho builder:
  - `ai_gemini_draft`
  - `ai_other`
  - `user_created`
- [x] `PrepareGeminiPosts` gắn `origin = ai_gemini_draft`.
- [x] `CreateGeminiPost` không còn tạo draft rời; giờ tạo `PostBuilder` + `Post`.
- [x] `CreatePostCommand` tự tạo `PostBuilder` khi request chưa có `postBuilderId`.
- [x] AI chat/web draft flow gắn `origin = ai_other`.
- [x] Agentic schedule runtime draft flow gắn `origin = ai_other`.
- [x] `PostBuilder` list/detail response trả thêm `originKind`.
- [x] `PostResponse`, `FacebookDraftPostResponse`, `ChatWebPostResult` trả thêm `postBuilderId`.
- [x] Migration thêm cột `origin_kind` và backfill builder cũ.

## Mục tiêu nghiệp vụ

Thay đổi này giải quyết 3 vấn đề:

- Backend cần biết một `PostBuilder` được sinh bởi Gemini draft flow, bởi AI flow khác, hay bởi user tạo tay.
- FE cần biết builder nào vừa được tạo khi user hoặc AI tạo draft để điều hướng đúng sang màn builder.
- Dữ liệu draft mới không nên tồn tại kiểu “post đơn lẻ không có builder”, vì điều đó làm flow chỉnh sửa/tổ chức draft thiếu nhất quán.

## Data model

### Bảng `post_builders`

Đã bổ sung cột mới:

| Column | Type | Ý nghĩa |
|---|---:|---|
| `origin_kind` | text nullable | nguồn sinh builder |

`workspace_id` đã tồn tại sẵn từ trước và không thay đổi ở feature này.

### Origin taxonomy

Hiện tại backend dùng đúng 3 giá trị:

- `ai_gemini_draft`
  - builder được tạo từ Gemini draft flow của AI generation.
- `ai_other`
  - builder được tạo từ AI flow khác không phải Gemini draft prepare/create, ví dụ agent chat web draft, agent draft thường, agent schedule runtime.
- `user_created`
  - builder được backend tự tạo khi user tạo draft thủ công qua `POST /api/Ai/posts` mà request không truyền `postBuilderId`.

## Luồng nghiệp vụ sau thay đổi

### 1. Gemini prepare flow

API:

- `POST /api/Ai/posts/prepare`
- `POST /api/AiGeneration/post-prepare`

Hành vi:

- tạo một `PostBuilder`;
- tạo các `Post` draft con theo từng social media item;
- set:
  - `post_builders.origin_kind = ai_gemini_draft`
  - `post_builders.workspace_id = request.workspaceId`
  - `posts.workspace_id = request.workspaceId`

Đây là flow builder-native từ đầu, chỉ bổ sung thêm metadata `origin`.

### 2. Gemini create single draft flow

API:

- `POST /api/AiGeneration/post`

Hành vi cũ:

- chỉ tạo một `Post` draft rời.

Hành vi mới:

- generate caption/title như cũ;
- tạo một `PostBuilder` mới;
- tạo một `Post` draft gắn `PostBuilderId`;
- set:
  - `post_builders.origin_kind = ai_gemini_draft`
  - `post_builders.resource_ids` từ `request.resourceIds`

Kết quả:

- draft Gemini đơn lẻ giờ vẫn có builder để FE quản lý thống nhất với prepare flow.

### 3. Manual create post flow

API:

- `POST /api/Ai/posts`

Hành vi mới:

- nếu request đã có `postBuilderId`:
  - backend giữ nguyên hành vi gắn post vào builder đó;
  - không tạo builder mới.
- nếu request chưa có `postBuilderId`:
  - backend tự tạo một `PostBuilder`;
  - set `origin_kind = user_created`;
  - seed `resource_ids` từ `content.resourceList`;
  - gắn `PostBuilderId` cho post mới.

Điều này giúp draft do user tạo tay không còn là draft “mồ côi” ngoài builder.

### 4. AI chat / web draft flow

Áp dụng cho:

- `GeminiAgentChatService` khi AI tạo draft text thông thường;
- `ChatWebPostService` khi AI tạo draft từ URL hoặc `web_search`;
- agent image/post flow khi cần tạo draft placeholder trước;
- agentic schedule runtime khi AI tạo runtime draft để publish tiếp.

Hành vi mới:

- các flow này gọi `CreatePostCommand` với `NewPostBuilderOrigin = ai_other`;
- nếu chưa có builder thì backend tự tạo builder mới;
- post được gắn vào builder đó ngay lúc tạo.

## Ảnh hưởng lên API response

### PostBuilder APIs

Các response sau có thêm `originKind`:

- `PostBuilderSummaryResponse`
- `PostBuilderDetailsResponse`

Hiện tại ảnh hưởng tới:

- `GET /api/Ai/post-builders`
- `GET /api/Ai/post-builders/workspace/{workspaceId}`
- `GET /api/Ai/post-builders/{postBuilderId}`

Đây là additive change, không thay đổi envelope `Result<T>`.

### Post / draft responses

Các response sau có thêm `postBuilderId`:

- `PostResponse`
- `FacebookDraftPostResponse`
- `ChatWebPostResult`

Mục đích:

- FE biết builder nào vừa được tạo cùng với draft;
- FE có thể mở đúng builder detail thay vì chỉ giữ `postId`.

## Migration và backfill

Migration:

- `20260502030534_AddPostBuilderOriginKind`

Thay đổi:

- thêm cột `post_builders.origin_kind`;
- backfill mọi row cũ đang `NULL` thành `ai_gemini_draft`.

### Vì sao backfill như vậy là an toàn?

Trong code trước thay đổi này, `PostBuilder` chỉ được tạo ở một chỗ:

- `PrepareGeminiPostsCommand`

Nghĩa là mọi builder lịch sử đều đến từ Gemini prepare flow, nên việc set toàn bộ builder cũ thành `ai_gemini_draft` là deterministic, không phải heuristic.

## Ghi chú thiết kế

### `workspaceId` của builder

`PostBuilder` đã có `workspace_id` từ trước thay đổi này.

Feature hiện tại không thêm field workspace mới, chỉ tận dụng logic đã có để:

- giữ `workspace_id` trên builder;
- đồng bộ `workspace_id` trên post con mới tạo.

### Vì sao lưu origin ở cấp builder thay vì chỉ suy luận từ post?

Lưu trực tiếp trên builder giúp:

- query list/detail builder không phải suy luận từ nhiều post con;
- tránh ambiguity khi trong tương lai một builder có thêm nhiều bước chỉnh sửa;
- FE có thể render badge hoặc lọc builder theo nguồn tạo ngay từ response hiện tại.

### Vì sao vẫn giữ `origin` là string thay vì enum C# / DB enum?

Hiện tại repo đang dùng nhiều taxonomy string cho nghiệp vụ tương tự như `resource origin`.

Giữ `origin_kind` là string:

- đơn giản cho migration;
- dễ tương thích với JSON/API hiện có;
- đủ linh hoạt nếu cần thêm loại origin mới sau này.

## File chính đã thay đổi

Các điểm chính trong code:

- `Backend/Microservices/Ai.Microservice/src/Domain/Entities/PostBuilder.cs`
- `Backend/Microservices/Ai.Microservice/src/Domain/Entities/PostBuilderOriginKinds.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Posts/Commands/CreatePostCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Posts/Commands/CreateGeminiPostCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Posts/Commands/PrepareGeminiPostsCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Agents/ChatWebPostService.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Agents/GeminiAgentChatService.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Migrations/20260502030534_AddPostBuilderOriginKind.cs`

## Hạn chế hiện tại

- `builder.ResourceIds` hiện được seed khi tạo builder, chưa có cơ chế đồng bộ ngược nếu user thay media của post sau đó.
- Chưa có API filter builder theo `originKind`.
- Chưa có analytics/reporting riêng cho tỷ lệ builder do AI hay user tạo.

## Kỳ vọng cho FE

FE nên ưu tiên dùng:

- `postBuilder.originKind` để hiển thị nguồn tạo builder;
- `postBuilderId` từ response draft/post để điều hướng sang màn builder tương ứng.

Không nên tiếp tục suy luận kiểu:

- “post có chatSessionId thì chắc là AI”;
- hoặc “builder cũ nào cũng là Gemini” ở phía client.

Suy luận đó giờ đã được backend chuẩn hóa thành field chính thức.
