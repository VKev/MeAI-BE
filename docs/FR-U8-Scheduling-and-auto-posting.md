# FR-U8 Scheduling and Auto Posting

## Mục tiêu

FR-U8 không còn là flow "tạo post trước, rồi mới schedule post đó".

Flow chính mới là:

- user gửi một prompt text cho AI;
- user cung cấp target publish, thời gian publish, timezone, và `maxContentLength`;
- backend tạo một `agentic` `PublishingSchedule` ngay lúc request;
- backend không tạo draft post trước;
- đến đúng `executeAtUtc`, runtime mới:
  - lấy fresh web search data;
  - enrich kết quả web;
  - có thể import media từ web thành user resources;
  - dùng RAG grounding từ social account đã chọn;
  - sinh content mới phù hợp với bối cảnh thời điểm đó;
  - tạo một `PostBuilder` runtime;
  - tạo một hoặc nhiều `Post` runtime theo platform group;
  - publish thẳng từng post runtime đến đúng targets của platform đó.

Tính năng này tồn tại để AI có thể tạo "future content" dựa trên dữ liệu tương lai, không bị đóng băng bởi một draft được sinh quá sớm.

## Product contract

### 1. Public entrypoint

Entry point chính của FR-U8 là:

`POST /api/Ai/agent/sessions/{sessionId}/messages`

Request body phải hỗ trợ:

- `message`: prompt text của user;
- `scheduleOptions.targets`: danh sách social account đích;
- `scheduleOptions.executeAtUtc`: thời điểm publish UTC;
- `scheduleOptions.timezone`: timezone user chọn;
- `scheduleOptions.maxContentLength`: hard cap cho `PostContent.Content` được sinh ở runtime.

Các field `imageOptions` và `videoOptions` cũng được controller agent hỗ trợ cho one-shot media generation, nhưng **không phải lane chính của FR-U8**. FR-U8 product lane vẫn là `scheduleOptions`.

Mẫu request:

```json
{
  "message": "Đến 6h tối hãy đăng bài tổng hợp tin nóng AI trong ngày, giữ giọng điệu ngắn gọn và dễ đọc.",
  "scheduleOptions": {
    "executeAtUtc": "2026-05-07T11:00:00Z",
    "timezone": "Asia/Ho_Chi_Minh",
    "maxContentLength": 280,
    "targets": [
      {
        "socialMediaId": "11111111-1111-1111-1111-111111111111",
        "isPrimary": true
      },
      {
        "socialMediaId": "22222222-2222-2222-2222-222222222222",
        "isPrimary": false
      }
    ]
  }
}
```

### 2. Validation-first

Agent vẫn là single-turn và validation-first:

- chỉ đọc message mới nhất;
- nếu prompt mơ hồ thì trả:
  - `action = "validation_failed"`
  - `validationError`
  - `revisedPrompt`
- trong trường hợp validation fail:
  - không tạo schedule;
  - không tạo post;
  - không phát sinh side effect nào.

Tuy nhiên, validation không nên quá máy móc. Agent được phép tự suy luận một số phần còn thiếu nếu ý định của user đã đủ rõ cho future scheduling.

Ví dụ hợp lệ:

- `"Sáng mai hãy đăng bài về đội tuyển vô địch World Cup năm nay."`
- `"Ngày mai hãy đăng bài về đội tuyển chiến thắng World Cup năm nay."`

Trong các trường hợp như vậy:

- user không cần biết trước đội nào thắng;
- backend không nên trả `validation_failed` chỉ vì kết quả thật chưa xảy ra ở thời điểm tạo schedule;
- agent được phép rewrite prompt sang dạng rõ hơn, ví dụ:
  - `"Hãy đăng bài về đội tuyển vô địch World Cup năm nay dựa trên kết quả thực tế tại thời điểm chạy."`

Chỉ nên trả `validation_failed` khi thông tin thiếu là loại không thể suy ra an toàn, ví dụ:

- `"hãy đăng bài về đội bóng tôi yêu"`
- `"hãy đăng bài về đội tuyển chiến thắng"`

vì các câu này chưa rõ đang nói về đội nào hoặc sự kiện nào.

### 3. Side effect khi prompt hợp lệ

Nếu prompt đủ rõ ràng và `scheduleOptions` hợp lệ:

- backend derive search query template ngay lúc create;
- backend resolve platform thật từ chính các `scheduleOptions.targets` đã chọn, không đoán bằng prompt text;
- backend có thể rewrite prompt nhẹ để làm rõ future intent nếu kết quả thực tế sẽ chỉ được biết ở runtime;
- backend persist:
  - `AgentPrompt`
  - `MaxContentLength`
  - stored search query template
  - `desiredPostType` cho runtime lane
  - full target list
  - `executeAtUtc`
  - `timezone`
- backend tự suy ra `PlatformPreference` từ target `primary`, hoặc target đầu tiên nếu không có `primary`;
- backend cũng phải suy ra publish shape tối thiểu từ target platforms:
  - nếu có target `TikTok` và user muốn đăng **video/reels** thì `desiredPostType = "reels"`; nếu user muốn đăng **ảnh carousel** thì `desiredPostType = "posts"` (TikTok hỗ trợ cả hai lane: video reels và photo carousel 1–35 ảnh);
  - nếu chỉ có `Threads` thì `desiredPostType` phải là `posts`;
  - nếu là `Facebook` hoặc `Instagram` thì có thể là `posts` hoặc `reels` tùy ý định user, nhưng runtime vẫn phải validate media compatibility trước khi publish;
- backend register runtime job / execution registration metadata;
- backend trả về response có:
  - `action = "future_ai_schedule_created"`
  - `scheduleId`

Không tạo draft post trước. Không cần preview là source of truth.

### 4. Schedule lifecycle

Đối với `agentic` schedule của FR-U8:

- user có thể `cancel` schedule trước thời điểm chạy;
- user có thể `re-activate` một schedule đã `cancel` nếu `executeAtUtc` vẫn còn ở tương lai;
- nếu `executeAtUtc` đã ở quá khứ thì backend không được re-activate schedule đó;
- khi re-activate thành công, backend phải đăng ký lại runtime job mới cho schedule;
- `cancel` hoặc `re-activate` không được tạo draft post trước;
- `cancel` không xoá schedule record, mà chuyển schedule sang trạng thái `cancelled`.

Public lifecycle endpoints hiện hành:

- `POST /api/Ai/schedules/{scheduleId}/cancel`
- `POST /api/Ai/schedules/{scheduleId}/activate`

## Runtime execution contract

Khi callback runtime xảy ra tại `executeAtUtc`, backend phải:

1. Nhận fresh web search payload từ workflow runtime.
2. Enrich thêm source hoặc media nếu có.
3. Chọn một grounding target để dùng cho RAG:
   - ưu tiên target khớp `PlatformPreference` nếu có;
   - nếu không có thì ưu tiên target `IsPrimary = true`;
   - nếu vẫn không có thì lấy target đầu tiên.
4. `WaitForRagReady`.
5. Re-index recent posts của grounding account.
6. Gọi recommendation pipeline để lấy:
   - page profile grounding;
   - account voice grounding;
   - recommendation summary từ past posts và knowledge.
7. Dùng Kie tool-calling loop thật (tối đa 12 turns) để model có thể gọi:
   - `web_search` — tìm kiếm web, tự động enrich kết quả, import media được tìm thấy;
   - `fetch_url` — fetch và enrich nội dung trang cụ thể;
   - `validate_media` — **bắt buộc gọi cho mọi URL ảnh từ web** trước khi import: HEAD check accessibility + gửi ảnh lên vision LLM để đánh giá nội dung có phù hợp với chủ đề post không. Video (import từ URL hoặc AI-generated) luôn được coi là phù hợp, không cần validate;
   - `import_media` — import image/video URL thành user resource; chỉ gọi cho ảnh đã qua `validate_media` với `suitability="suitable"`, hoặc cho video URL;
   - `generate_image` — sinh ảnh mới bằng AI khi web không có ảnh phù hợp (xem §"Media generation trong agentic runtime");
   - `create_runtime_post_draft` — finalize draft output (bắt buộc gọi cuối cùng).
8. Generate runtime post draft từ:
   - `AgentPrompt`
   - fresh web search
   - recommendation summary
   - page profile grounding
9. Enforce `MaxContentLength` như hard cap trên `PostContent.Content`.
10. Nhóm active targets theo platform.
11. Derive publish constraint cho từng platform group trước khi tạo post runtime.
12. Yêu cầu model/runtime draft sinh đúng `postType` và đúng loại media cho group đó.
13. Validate draft runtime trước `CreatePostCommand`; nếu draft không publish được lên platform group đó thì fail sớm ở schedule execution lane.
14. Tạo một runtime `PostBuilder`.
15. Tạo một runtime `Post` cho mỗi platform group trong cùng builder.
16. Publish thẳng từng runtime `Post` tới đúng targets của platform group đó.

Quan trọng:

- Runtime execution **không** tạo thêm nested schedule mới.
- Runtime execution **không** dùng kiểu "một post fan-out cho mọi platform" nữa nếu schedule có nhiều platform khác nhau.
- Publish xảy ra ngay trong execution lane khi tới giờ chạy.

### Platform compatibility rules

Runtime lane của FR-U8 phải tôn trọng đúng giới hạn publish thật của từng platform:

- `TikTok photo carousel` (`postType = posts`):
  - **Lane mặc định** cho TikTok khi user muốn đăng ảnh.
  - Runtime phải import hoặc generate từ 1 đến 35 **ảnh** (image resources).
  - Không được có video resource trong draft này.
  - Agent được phép gọi `generate_image` nhiều lần (mỗi lần = 1 slide) hoặc import nhiều ảnh web cùng lúc.
  - Publish đến TikTok qua endpoint `POST /v2/post/publish/content/init/` (PHOTO type).
- `TikTok reels` (`postType = reels`):
  - Runtime phải có đúng **1 video resource**.
  - Draft text-only, image-only, hoặc nhiều video → schedule fail sớm.
  - Publish đến TikTok qua endpoint `POST /v2/post/publish/video/init/`.
- `Facebook reels`:
  - `postType = reels`; phải có đúng 1 video.
- `Facebook posts`:
  - Có thể là text-only.
  - Nếu có media thì không được mix image + video.
  - Không được nhiều video trong cùng một publish.
- `Instagram reels`:
  - `postType = reels`; phải có đúng 1 video.
- `Instagram posts`:
  - Hiện tại chỉ hỗ trợ đúng 1 media item (image hoặc video).
- `Threads`:
  - Hỗ trợ text-only hoặc đúng 1 media item.
  - Runtime không được tạo nhiều media cho một draft Threads.

Điểm quan trọng của FR-U8 là các rule này phải được encode ngay trong agentic runtime lane, không chờ đến bước `PublishToTargetConsumer` mới phát hiện mismatch.

## Media generation trong agentic runtime

### `generate_image` — có sẵn, chạy đồng bộ trong tool loop

`AgenticRuntimeContentService` inject sẵn `IImageGenerationClient` (implementation: `OpenRouterImageGenerationClient`). Khi Kie model gọi tool `generate_image` trong tool loop:

1. Backend gọi OpenRouter `/chat/completions` với model image-gen (mặc định `openai/gpt-5.4-image-2`) trả về **data URL** (`data:image/png;base64,...`).
2. Data URL được upload thẳng vào user resource system qua `IUserResourceService.CreateResourcesFromUrlsAsync` với `resourceType = "image"`.
3. `resourceId` trả về được thêm vào pool resource của draft ngay trong cùng tool turn.
4. Model nhận lại `resourceId` và `presignedUrl` để finalize draft.

**Tool schema của `generate_image`**:
```json
{
  "prompt": "Detailed visual description...",
  "referenceImageUrls": ["..."],   // optional, max 3
  "styleHint": "photorealistic"    // optional
}
```

**Khi nào model nên dùng `generate_image`**:
- Không tìm thấy ảnh trên web.
- `validate_media` trả về `suitability="unsuitable"` cho các ảnh đã tìm được.
- `validate_media` báo URL không accessible.
- Post cần minh họa tùy chỉnh (infographic, branded image, v.v.).
- TikTok carousel thiếu ảnh — có thể gọi `generate_image` nhiều lần, mỗi lần sinh 1 slide (tối đa 35 slides).

**Chi phí & billing**: `generate_image` trong agentic runtime **không** charge coin — cost được hấp thụ vào cost OpenRouter API của schedule execution. Khác với `CreateChatVideoCommand` hay `CreateImageCommand` ở chat lane (có debit coin trước).

**Không có `styleHint` fallback video**: `generate_image` chỉ sinh ảnh tĩnh. Không thể sinh video từ tool này.

---

### `validate_media` — image suitability check qua vision AI

Từ bản cập nhật này, `validate_media` không chỉ check HTTP accessibility mà còn:

- **Với ảnh (image/\*)**: Gửi ảnh lên `IMultimodalLlmClient` (vision LLM) cùng với chủ đề post và platform để đánh giá nội dung. LLM trả về `SUITABLE: <reason>` hoặc `UNSUITABLE: <reason>`. Backend map sang field `suitability = "suitable" | "unsuitable"`.
- **Với video (video/\*)**: Luôn trả về `suitability = "suitable"` — video được tạo từ prompt hoặc import từ nguồn biết trước nên luôn phù hợp về nội dung.
- **Khi vision LLM lỗi**: Fallback về `suitability = "suitable"` để không block import vì lỗi transient.

**Response schema mới** của `validate_media`:
```json
{
  "validationResults": [
    {
      "url": "https://...",
      "status": 200,
      "ok": true,
      "contentType": "image/jpeg",
      "suitability": "suitable",       // "suitable" | "unsuitable" | "unknown"
      "suitabilityReason": "...",
      "hint": "Image is accessible and suitable for the post — safe to import."
    }
  ]
}
```

**Workflow chuẩn cho ảnh web**:
```
web_search → validate_media (ALWAYS) → import_media (chỉ suitable) hoặc generate_image (nếu không có suitable) → create_runtime_post_draft
```

**Video không cần validate_media** — AI-generated video hoặc video URL từ nguồn biết trước luôn phù hợp. Gọi thẳng `import_media`.

---

### Video generation (Veo/Kie) — **KHÔNG thể dùng trong synchronous tool loop**

Hệ thống có tính năng tạo video qua Veo (`IVeoVideoService`) và Kie (`CreateChatVideoCommand`), nhưng **không được nhúng vào agentic schedule tool loop** vì:

| Lý do | Chi tiết |
|---|---|
| **Async callback-based** | Veo/Kie video generation không trả về video URL ngay lập tức. Backend phát `VideoGenerationStarted` message vào RabbitMQ, consumer gọi API Kie/Veo, video hoàn thành sau vài phút → callback `VideoGenerationCompleted`. |
| **Tool loop là synchronous** | `AgenticRuntimeContentService` chạy đến 12 turns gọi Kie Responses API tuần tự. Không có cơ chế await async callback giữa các turns. |
| **Billing pre-debit** | Video generation phải debit coin trước khi enqueue. Agentic runtime không có context billing phù hợp để debit mid-loop. |
| **Timeout** | Mỗi tool call phải hoàn thành trong vòng vài giây. Video generation mất 30–300 giây. |

**Kết luận**: Nếu schedule target là TikTok reels (cần video), agentic runtime chỉ có thể **import video URL từ web** (qua `import_media` sau khi tìm được URL video công khai). Không thể tự sinh video từ prompt. Đây là giới hạn có chủ ý — video generation cho schedule phải đi qua chat lane thông thường trước, sau đó user attach video resource vào schedule.

**Roadmap** (chưa implement): Có thể bổ sung một `generate_video_async` tool mà:
1. Pre-debit coin khi schedule bắt đầu execute.
2. Enqueue Veo job với `linkedScheduleId`.
3. Khi `VideoGenerationCompleted` callback, attach resource vào post draft và trigger publish.
Nhưng đây là architecture phức tạp hơn — hiện tại chưa có.

---

## RAG và search

FR-U8 runtime phải kết hợp cả hai lớp grounding:

- `web search`: để lấy context mới tại thời điểm thực thi;
- `RAG microservice`: để lấy page profile, voice, pattern nội dung, và past-post references của account.

Provider/model/runtime hiện tại:

- AI provider cho runtime lane: **Kie**
- Endpoint: `/codex/v1/responses`
- Default model: `gpt-4o-mini`
- Structured output được lấy qua **function calling**, không ép model trả text JSON rồi parse.

Search query template phải được derive và persist ngay lúc create schedule, không để runtime mới tự nghĩ lại từ đầu.

Prompt lưu trong schedule không nhất thiết phải giống 100% câu user gõ. Agent được phép lưu phiên bản đã được làm rõ hơn nếu:

- vẫn giữ nguyên ý định của user;
- không tự ý đổi chủ đề;
- chỉ bổ sung các chi tiết mang tính cấu trúc như:
  - `"dựa trên kết quả thực tế tại thời điểm chạy"`
  - `"đội tuyển vô địch"` thay cho `"đội tuyển chiến thắng"` khi đang nói về một sự kiện tương lai.

RAG failure policy:

- nếu RAG query hoặc index thất bại tại runtime, backend được fallback về lane web-search-only;
- schedule không nên bị mất chỉ vì RAG tạm thời lỗi;
- fallback reason cần được lưu trong `ExecutionContextJson` để debug.

## Data model expectations

`PublishingSchedule` agentic cần có các trường cần thiết cho future publishing:

- `Mode = "agentic"`
- `AgentPrompt`
- `MaxContentLength`
- stored search query template
- `desiredPostType` trong execution context để runtime biết đang phải sinh `posts` hay `reels`
- `ExecutionContextJson` cho state runtime, n8n, và debug

Không cần có `PublishingScheduleItem` ở thời điểm create. Item sẽ được tạo khi runtime thực sự sinh ra post.

`ExecutionContextJson` cũng là nơi backend có thể cập nhật metadata phục vụ re-activation, ví dụ:

- runtime job id mới nhất;
- thời điểm register gần nhất;
- callback/runtime debug state gần nhất.
- `DesiredPostType`
- `RuntimePostBuilderId`
- `RuntimePostIds`
- `RuntimePostId` đầu tiên để backward compatibility

Ngoài `ExecutionContextJson`, response schedule hiện cũng nên expose typed metadata cho runtime artifact:

- `runtimePostBuilderId`
- `runtimePostIds`

để frontend không phải tự parse JSON debug blob.

## Runtime post builder và resource semantics

FR-U8 runtime không chỉ sinh post; nó còn phải giữ đúng semantics của post builder:

- một `PostBuilder` có thể chứa nhiều `Post` con cho nhiều platform;
- `PostBuilder.ResourceIds` là pool resource dùng chung;
- `Post.Content.ResourceList` là resource riêng của từng post/platform.

Điều này cho phép:

- một số platform dùng chung cùng media;
- một số platform dùng media khác nhau;
- web-imported media hoặc AI-generated media được dùng lại trong cùng builder.

Publish lane cũng phải hiểu điều này:

- nếu `Post.Content.ResourceList` rỗng, backend được phép fallback sang `PostBuilder.ResourceIds`;
- như vậy post con vẫn publish được dù media đang được giữ ở builder-level.

Tuy nhiên với FR-U8 agentic runtime hiện tại, draft nên mang `Post.Content.ResourceList` đúng ngay từ đầu cho từng platform post, vì validator platform compatibility chạy trước khi publish.

Lưu ý giới hạn hiện tại:

- import lại cùng một URL hiện chưa đảm bảo reuse đúng cùng `resourceId`; hệ thống vẫn có thể tạo resource mới cho cùng source URL ở các request khác nhau.

## Legacy lane

`POST /api/Ai/schedules` mode `fixed_content` vẫn tồn tại cho use case legacy hoặc manual scheduling.

But đối với FR-U8 product flow, lane chính là:

- chat tạo `agentic` schedule trước;
- AI runtime tạo content sau.

## Frontend Integration & Real-time Progress Tracking (SignalR)

Để nâng cao trải nghiệm người dùng, hệ thống cung cấp luồng theo dõi tiến trình thực thi của **Agentic Publishing Schedule** theo thời gian thực (real-time push) kết hợp với cơ chế lưu log lịch sử (historical logs) tương tự như hệ thống **AI Recommendation / Draft Post Generation**.

---

### 1. Chi tiết các API Endpoint phục vụ Frontend

Frontend cần phối hợp gọi các REST API dưới đây để hiển thị cấu hình, trạng thái lịch sử và quản lý vòng đời của Publishing Schedule:

#### 1.1 Lấy danh sách Schedules (`GET`)
- **Endpoint**: `GET /api/Ai/schedules`
- **Query Parameters**:
  - `workspaceId` (Guid, optional): Bộ lọc theo Workspace ID.
  - `status` (string, optional): Trạng thái của Schedule (`"Pending"`, `"Executing"`, `"Publishing"`, `"Completed"`, `"Failed"`, `"Cancelled"`).
  - `limit` (int, optional): Giới hạn số lượng bản ghi trả về.
- **Headers**: `Authorization: Bearer <token>`
- **Response**: Trạng thái danh sách được bọc trong `Result<IReadOnlyList<PublishingScheduleResponse>>`.
  ```json
  {
    "value": [
      {
        "id": "019e4b3d-fb37-7d23-879f-1f19a9b5aae9",
        "name": "Tin Nóng AI Mỗi Ngày",
        "mode": "agentic",
        "status": "Completed",
        "executeAtUtc": "2026-05-21T18:00:00Z",
        "timezone": "Asia/Ho_Chi_Minh",
        "platformPreference": "facebook",
        "agentPrompt": "Hãy tổng hợp tin nóng AI trong ngày...",
        "maxContentLength": 280,
        "runtimePostBuilderId": "019e4b3d-fb37-7d23-879f-1f19a9b5bbbb",
        "runtimePostIds": ["019e4b3d-fb37-7d23-879f-1f19a9b5accc"]
      }
    ],
    "isSuccess": true,
    "error": { "code": "", "description": "" }
  }
  ```

#### 1.2 Lấy chi tiết một Schedule (`GET`)
- **Endpoint**: `GET /api/Ai/schedules/{scheduleId}`
- **Response**: Trả về `Result<PublishingScheduleResponse>`. Đây là API chính giúp lấy trường `executionContextJson` chứa toàn bộ lịch sử tiến trình chạy.
  - Khi status là `Completed` hoặc `Publishing`: Trường `runtimePostBuilderId` và `runtimePostIds` sẽ chứa ID của Post Builder và các Post đã sinh để frontend có thể điều hướng người dùng xem bài viết đã tạo.

#### 1.3 Hủy Schedule (`POST`)
- **Endpoint**: `POST /api/Ai/schedules/{scheduleId}/cancel`
- **Response**: Trả về `Result<bool>`. Dùng để dừng schedule đang ở trạng thái `Pending` trước giờ chạy.

#### 1.4 Kích hoạt lại Schedule (`POST`)
- **Endpoint**: `POST /api/Ai/schedules/{scheduleId}/activate`
- **Response**: Trả về `Result<bool>`. Chỉ kích hoạt được nếu thời điểm `executeAtUtc` vẫn nằm trong tương lai.

---

### 2. Cấu trúc dữ liệu Log (`ExecutionContextJson`)

Khi gọi API chi tiết hoặc nhận event đẩy từ SignalR, frontend sẽ nhận được trường `ExecutionContextJson`. Sau khi parse chuỗi JSON này, bạn sẽ nhận được các thông tin cực kỳ chi tiết sau:

- `currentStep` (string): Định danh bước hiện tại đang chạy.
- `currentStepStatus` (string): Trạng thái của bước hiện tại (`"Running"`, `"Completed"`, `"Failed"`, `"Skipped"`).
- `currentStepMessage` (string): Mô tả chi tiết hành động đang thực hiện (bằng tiếng Việt hoặc tiếng Anh).
- `steps` (array): Danh sách lịch sử toàn bộ các bước đã và đang chạy trong phiên này:
  - `step` (string): Mã định danh bước (dùng để map UI).
  - `status` (string): Trạng thái (`"Running"`, `"Completed"`, `"Failed"`, `"Skipped"`).
  - `message` (string): Nội dung log chi tiết.
  - `timestampUtc` (string/ISO-Date): Thời gian log bước.

---

### 3. Tích hợp Real-time SignalR (Code mẫu TypeScript)

Frontend kết nối tới SignalR Hub thông qua API Gateway tại route `/api/Notification/hubs/notifications` (hoặc `/hubs/notifications`). Dưới đây là code mẫu tích hợp chuẩn sử dụng thư viện `@microsoft/signalr`:

```typescript
import * as signalR from "@microsoft/signalr";

interface ProgressLogStep {
  step: string;
  status: "Running" | "Completed" | "Failed" | "Skipped";
  message: string;
  timestampUtc: string;
}

interface ScheduleNotificationPayload {
  scheduleId: string;
  workspaceId: string;
  userId: string;
  status: string;
  currentStep: string;
  currentStepStatus: "Running" | "Completed" | "Failed" | "Skipped";
  currentStepMessage: string;
  steps: ProgressLogStep[];
  createdAt: string;
}

interface SignalRNotification {
  notificationId: string;
  type: "ai.publishing_schedule.thinking" | "ai.publishing_schedule.completed" | "ai.publishing_schedule.failed";
  title: string;
  message: string;
  payloadJson: string; // Cần parse chuỗi này để lấy ScheduleNotificationPayload
}

class PublishingScheduleTracker {
  private connection: signalR.HubConnection | null = null;

  public async connect(accessToken: string, onProgressUpdate: (payload: ScheduleNotificationPayload) => void) {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl("/api/Notification/hubs/notifications", {
        accessTokenFactory: () => accessToken,
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Lắng nghe sự kiện đẩy từ server
    this.connection.on("ReceiveNotification", (notification: SignalRNotification) => {
      if (
        notification.type === "ai.publishing_schedule.thinking" ||
        notification.type === "ai.publishing_schedule.completed" ||
        notification.type === "ai.publishing_schedule.failed"
      ) {
        try {
          const payload: ScheduleNotificationPayload = JSON.parse(notification.payloadJson);
          onProgressUpdate(payload);
        } catch (e) {
          console.error("Lỗi parse payloadJson từ thông báo SignalR:", e);
        }
      }
    });

    try {
      await this.connection.start();
      console.log("Đã kết nối thành công tới SignalR Notification Hub.");
    } catch (err) {
      console.error("Lỗi kết nối tới SignalR Hub:", err);
      setTimeout(() => this.connect(accessToken, onProgressUpdate), 5000);
    }
  }

  public async disconnect() {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      console.log("Đã ngắt kết nối SignalR.");
    }
  }
}

export default PublishingScheduleTracker;
```

---

### 4. Thiết kế giao diện (UI/UX Blueprint cho Progress Log Timeline)

Để mang lại giao diện trực quan và trải nghiệm "Wow" cao cấp giống như hệ thống AI Recommendation, frontend nên render các bước tiến trình thành dạng **Timeline/Stepper** (ví dụ: nằm trong một Drawer hoặc Modal theo dõi tiến độ chạy).

#### 4.1 Bảng ánh xạ mã bước (`step`) ra giao diện người dùng
Dưới đây là đặc tả ánh xạ từ mã `step` nhận từ API/SignalR ra nhãn hiển thị và icon gợi ý:

| Mã Step | Tên Hiển Thị (UI Label) | Icon Gợi Ý | Mô Tả Trạng Thái Chạy |
|---|---|---|---|
| `web_search` | Tìm kiếm dữ liệu thời gian thực | 🌐 `Search / Global` | AI phân tích cấu hình, khởi chạy Web Search để quét tin tức mới nhất về chủ đề yêu cầu. |
| `rag_ready` | Kết nối AI Knowledge Base | 🤖 `Cpu / Sparkles` | Kiểm tra trạng thái sẵn sàng của dịch vụ RAG sidecar. |
| `indexing_grounding` | Phân tích Brand Voice | 📚 `BookOpen / Pen` | Tự động quét và nạp các bài đăng cũ của tài khoản mạng xã hội mục tiêu vào bộ nhớ vector của AI. |
| `recommendation_generation` | Lập ý tưởng & Định hướng | 💡 `Lightbulb` | Kết hợp dữ liệu Web và Brand Voice để tìm kiếm ý tưởng cá nhân hóa, định hình giọng điệu phù hợp. |
| `draft_generation_<platform>` | Soạn thảo nội dung nháp (`<platform>`) | 📝 `FileText` | LLM chạy tool loop (tìm web, validate ảnh, import/generate ảnh, finalize draft). Ví dụ: `draft_generation_tiktok` sẽ sinh carousel ảnh nếu postType=posts, hoặc tìm video nếu postType=reels. |
| `post_creation_<platform>` | Tạo bản ghi bài viết (`<platform>`) | 💾 `Database` | Lưu trữ bài viết thành công vào database nội bộ của hệ thống dưới dạng bản nháp. |
| `asset_linking` | Đồng bộ hóa tài nguyên | 🔗 `Paperclip / Image` | Gắn và đồng bộ hóa các hình ảnh/video được tải xuống hoặc AI sinh ra vào Post Builder chung. |
| `publishing` | Đăng bài trực tiếp | 🚀 `Send / Share2` | Đẩy thẳng nội dung hoàn thiện lên kênh mạng xã hội thật. TikTok photo carousel → `PublishCarouselAsync`; TikTok reels → `PublishAsync` (video). Toàn bộ tiến trình hoàn tất! |

#### 4.2 Nguyên tắc hiển thị trạng thái của Step (UI States)
Mỗi step trong danh sách `steps` hoặc `currentStep` cần hiển thị đúng icon trạng thái động:
- **`Running`**: 
  - Render icon loading spinner dạng xoay tròn, thêm hiệu ứng text mờ nhẹ hoặc nhấp nháy (skeleton pulse).
  - Vị trí scroll của log drawer nên tự động cuộn xuống dưới cùng để người dùng thấy rõ bước đang chạy.
- **`Completed`**:
  - Render icon vòng tròn tích xanh (green checkmark).
  - Hiển thị nội dung chi tiết trong `message` (ví dụ: báo cáo số lượng tin tức quét được, mã bài viết tạo ra...).
- **`Failed`**:
  - Render icon vòng tròn dấu chéo đỏ (red warning icon).
  - Show message lỗi chi tiết ra ngoài để người dùng biết lý do (ví dụ: lỗi tài khoản mạng xã hội hết hạn token, lỗi định dạng video không đúng chuẩn...).
- **`Skipped` / Chưa chạy**:
  - Render icon vòng tròn màu xám mờ hoặc chấm tròn nhỏ.
  - Text mô tả mờ (opacity 0.5) biểu thị bước này chưa bắt đầu chạy.

#### 4.3 Hành động sau khi Hoàn thành (Post-Execution Actions)
Khi nhận sự kiện `ai.publishing_schedule.completed` hoặc bước `publishing` chuyển sang `Completed`:
1. Hiển thị một banner chúc mừng nổi bật ở góc phải màn hình hoặc trên cùng drawer tiến trình.
2. Tự động hiển thị nút **"Xem bài viết vừa đăng"** (View Post) hoặc **"Xem trong Post Builder"** kết nối trực tiếp bằng cách điều hướng route tới:
   - Trang Post Builder: `/workspaces/{workspaceId}/posts/builder/{runtimePostBuilderId}`
   - Trang chi tiết Post: `/workspaces/{workspaceId}/posts/{runtimePostId}` (lấy từ phần tử đầu tiên của mảng `runtimePostIds` hoặc trường `runtimePostId` trong response API).

> [!NOTE]
> Nhờ cơ chế SignalR thời gian thực kết hợp trường `ExecutionContextJson` đã được thiết kế đồng bộ, frontend chỉ cần binding trực tiếp mảng dữ liệu này mà không cần thiết lập bất kỳ vòng lặp polling HTTP nào, giúp tối ưu hóa băng thông mạng và đem lại trải nghiệm mượt mà, cao cấp cho người dùng.
