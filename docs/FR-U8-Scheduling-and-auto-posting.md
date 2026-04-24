# FR-U8 Scheduling and Auto Posting

## Mục tiêu tài liệu

Tài liệu này chốt lại đầy đủ yêu cầu nghiệp vụ mà stakeholder đã nêu cho `FR-U8`, đồng thời mô tả kiến trúc mục tiêu phù hợp với codebase hiện tại của `MeAI-BE`.

Từ thời điểm này, `FR-U8` không chỉ còn là "schedule một post có sẵn", mà phải bao phủ đủ 4 ý:

- `FR-U8.1`: người dùng tạo schedule chỉ định thời điểm cần đăng.
- `FR-U8.2`: người dùng gắn một hoặc nhiều post/video có sẵn với schedule đó.
- `FR-U8.3`: AI có thể nhận yêu cầu tự nhiên, hỏi ngược nếu thiếu dữ liệu, tự chọn hoặc tự tạo nội dung đúng lúc cần chạy, và có thể dùng web search để lấy dữ liệu online tại thời điểm thực thi.
- `FR-U8.4`: hệ thống tự động publish nội dung đúng thời điểm lên đúng social account mà user đã chỉ định hoặc đã được agent resolve trước đó.

Tài liệu này cũng chốt thêm một yêu cầu nền tảng:

- Người dùng phải có thể trò chuyện trực tiếp với AI Gemini.
- Agent phải dùng `Google.GenAI` + `Microsoft.Extensions.AI` cho bài toán single-agent có function/tool calling.
- Tooling phải đủ để agent tự thu thập ngữ cảnh người dùng như workspace, social account đã liên kết, post/video có sẵn, schedule hiện có.
- `n8n` là execution plane cho `web_search` và cho các job cần "đợi tới đúng thời điểm rồi mới lấy dữ liệu web".

## Yêu cầu nghiệp vụ đã chốt với stakeholder

### 1. Fixed-content scheduling

Hệ thống phải cho phép user:

- chọn một hoặc nhiều bài post/video đã có trong hệ thống;
- gắn các item đó vào một schedule;
- chọn thời điểm cụ thể;
- chọn đúng nơi đăng;
- để hệ thống tự đăng khi đến giờ.

Ví dụ:

- "Đăng 3 post đã có trong workspace A vào 17:00 hôm nay lên fanpage Facebook X."
- "Đăng 1 reel và 1 post ảnh vào 09:00 sáng mai lên Instagram account Y."

### 2. Agentic scheduling bằng hội thoại tự nhiên

Hệ thống phải cho phép user mô tả ý định bằng prompt tự nhiên, ví dụ:

> "Vào 5h chiều hãy tra kết quả xổ số miền bắc rồi đăng nó lên fb."

Khi đó hệ thống phải:

- hiểu đây là một yêu cầu schedule;
- hỏi ngược nếu thiếu thông tin;
- biết lấy các social account mà user đã link;
- biết nếu user có nhiều Facebook account thì phải hỏi rõ user muốn account nào;
- biết nếu workspace chưa có account phù hợp thì phải báo thiếu điều kiện;
- biết đợi đến đúng 17:00 theo timezone của user;
- tới thời điểm đó mới tra cứu dữ liệu online của ngày hôm đó;
- tạo hoặc assemble nội dung;
- publish lên đúng account đã resolve trước đó.

### 3. Trò chuyện trực tiếp với Gemini

Ngoài scheduling, user phải có thể chat trực tiếp với Gemini như một assistant trong hệ thống.

Assistant này:

- có thể trả lời bình thường nếu user chỉ hỏi/chỉ đạo;
- có thể dùng tools nếu cần;
- có thể hỏi tiếp user cho tới khi đủ slot dữ liệu;
- có thể tạo post, tạo schedule, liệt kê account social, xem lại schedule đã tạo.

## Hiện trạng repo tại ngày 2026-04-23

### Đã có trong repo

Repo hiện đã có một phần nền tảng tốt cho `FR-U8.1` và `FR-U8.4`:

- `Ai.Microservice` đã có `POST /api/Ai/posts/{postId}/schedule`.
- `Ai.Microservice` đã có API tạo/sửa post draft:
  - `POST /api/Ai/posts`
  - `PUT /api/Ai/posts/{postId}`
  - request/response đã hỗ trợ `chatSessionId` để link post với một chat session.
- `Ai.Microservice` đã có API lấy post theo chat session:
  - `GET /api/Ai/posts/chat-session/{chatSessionId}`
- `Post` đã có các field:
  - `ChatSessionId`
  - `ScheduleGroupId`
  - `ScheduledAtUtc`
  - `ScheduleTimezone`
  - `ScheduledSocialMediaIds`
  - `ScheduledIsPrivate`
- `ScheduledPostPublishingWorker` đang poll các post tới hạn.
- `ScheduledPostDispatchService` đang claim post tới hạn và gọi lại `PublishPostsCommand`.
- Publish flow async qua `MassTransit` + `RabbitMQ` + `PublishToTargetConsumer` đã tồn tại.
- `User.Microservice` đã có API lấy social accounts và workspace-social links.
- `Ai.Microservice` đã có gRPC client `UserSocialMediaService` để resolve social media ids.
- `Ai.Microservice` đã có `ChatSession` / `Chat` CRUD cơ bản.
- Gemini agent đã có tool tạo/sửa draft post:
  - `create_post`
  - `update_post`
  - `get_posts(currentChatOnly: true)`
- Repo đã có hạ tầng `n8n` trong:
  - `Backend/Compose/docker-compose.yml`
  - `Backend/Kubernetes/manifests/05-n8n.yaml`
  - Terraform service definitions cho `n8n`

### Giới hạn còn lại để product hóa FR-U8

Phần hiện tại đã có foundation chính cho scheduling, agent tool calling và `n8n` runtime, nhưng vẫn còn các giới hạn cần hardening/productization:

- chưa có khái niệm schedule dành cho `video` hoặc mixed items;
- chưa có retry/pause/resume first-class cho `agentic` schedule;
- chưa có notification flow đầy đủ cho `needs_user_action`;
- chưa có end-to-end integration test chạy thật qua `n8n` container + Brave Search sandbox;
- entity `Chat` hiện vẫn chưa phải transcript model hoàn chỉnh cho role-based history kiểu `user/assistant/tool`; implementation hiện dùng metadata tạm trong `Chat.Config`.

## Quyết định kiến trúc

### 1. Không nhồi toàn bộ FR-U8 vào schedule field của `Post`

Field schedule trên `Post` vẫn hữu ích và nên giữ lại cho use case đơn giản:

- user chọn một post có sẵn;
- đặt giờ;
- đăng tự động.

Nhưng với `FR-U8` đầy đủ, cần bổ sung một aggregate cấp cao hơn, tạm gọi là `PublishingSchedule`.

Lý do:

- `FR-U8.2` yêu cầu một schedule có thể chứa nhiều post/video;
- `FR-U8.3` yêu cầu một schedule có thể chưa có post sẵn tại thời điểm tạo;
- cần lưu target accounts, execution mode, prompt, query template, tool requirements, execution history;
- cần trạng thái rõ ràng cho agentic job: `draft`, `awaiting_user_input`, `scheduled`, `waiting_for_execution`, `executing`, `publishing`, `completed`, `failed`, `needs_user_action`, `cancelled`.

### 2. Chia FR-U8 thành 2 lane thực thi

#### Lane A: Fixed-content schedule

Dùng cho:

- schedule 1 hoặc nhiều post/video đã có sẵn;
- nội dung không cần web grounding ở runtime;
- publish deterministic.

Lane này tái sử dụng tối đa publish flow hiện có.

#### Lane B: Agentic live-content schedule

Dùng cho:

- nội dung chỉ được quyết định hoặc hoàn thiện tại thời điểm chạy;
- cần tra web đúng thời điểm;
- cần agent tự chọn/tạo post rồi mới publish.

Lane này dùng:

- Gemini chat agent trong `Ai.Microservice`;
- tool/function calling;
- `n8n` để đợi đến giờ và chạy `web_search`;
- Brave Search để lấy dữ liệu online;
- callback từ `n8n` về `Ai.Microservice`;
- rồi `Ai.Microservice` dùng lại `CreatePost` / `PublishPostsCommand`.

## Thiết kế mục tiêu cho FR-U8.1 và FR-U8.2

### Aggregate mới đề xuất

Thêm aggregate `PublishingSchedule` thay vì chỉ dựa vào `Post.Schedule*`.

#### `PublishingSchedule`

Field tối thiểu:

- `Id`
- `UserId`
- `WorkspaceId`
- `Name`
- `Mode`
  - `fixed_content`
  - `agentic`
- `Status`
- `Timezone`
- `ExecuteAtUtc`
- `ResolvedTargetSocialMediaIds`
- `PlatformPreference`
- `IsPrivate`
- `AgentPrompt`
- `ExecutionContextJson`
- `CreatedBy`
  - `user`
  - `agent`
- `CreatedAt`
- `UpdatedAt`
- `LastExecutionAt`
- `NextRetryAt`
- `ErrorCode`
- `ErrorMessage`

#### `PublishingScheduleItem`

Field tối thiểu:

- `Id`
- `ScheduleId`
- `ItemType`
  - `post`
  - `video`
- `ItemId`
- `SortOrder`
- `ExecutionBehavior`
  - `publish_all`
  - `publish_first_successful`
  - `publish_in_sequence`

#### `PublishingScheduleTarget`

Field tối thiểu:

- `Id`
- `ScheduleId`
- `SocialMediaId`
- `Platform`
- `TargetLabel`
- `IsPrimary`

### API mục tiêu

#### Fixed-content schedule APIs

- `POST /api/Ai/schedules`
- `GET /api/Ai/schedules`
- `GET /api/Ai/schedules/{scheduleId}`
- `PUT /api/Ai/schedules/{scheduleId}`
- `POST /api/Ai/schedules/{scheduleId}/cancel`
- `POST /api/Ai/schedules/{scheduleId}/activate`

Request `POST /api/Ai/schedules` tối thiểu:

```json
{
  "workspaceId": "11111111-1111-1111-1111-111111111111",
  "name": "17h daily lottery post",
  "mode": "fixed_content",
  "executeAtUtc": "2026-04-23T10:00:00Z",
  "timezone": "Asia/Ho_Chi_Minh",
  "targetSocialMediaIds": [
    "22222222-2222-2222-2222-222222222222"
  ],
  "items": [
    {
      "itemType": "post",
      "itemId": "33333333-3333-3333-3333-333333333333",
      "sortOrder": 1
    },
    {
      "itemType": "post",
      "itemId": "44444444-4444-4444-4444-444444444444",
      "sortOrder": 2
    }
  ],
  "isPrivate": false
}
```

### Validation rules

`FR-U8.1/U8.2` phải enforce ít nhất các rule sau:

- `executeAtUtc` phải ở tương lai;
- `timezone` phải hợp lệ;
- schedule phải thuộc đúng `workspace` của user;
- target social accounts phải thuộc user;
- nếu schedule là fixed-content thì phải có ít nhất 1 item;
- mọi item phải tồn tại và thuộc user/workspace đó;
- nếu item là `video`, publish pipeline của platform tương ứng phải hỗ trợ;
- nếu target platform không hỗ trợ loại content đó thì reject ngay khi tạo schedule;
- nếu user chọn nhiều target accounts thì hệ thống phải publish đúng toàn bộ danh sách đã lưu;
- nếu user sửa hoặc xóa account sau khi schedule đã tạo thì lần chạy phải chuyển sang `needs_user_action` thay vì publish sai đích.

### Quan hệ với phần đã có

Phần schedule đang gắn trên `Post` nên được xem là:

- shortcut API cho single-post scheduling;
- backward-compatible path cho frontend hiện tại;
- có thể được nội bộ map sang `PublishingSchedule` trong giai đoạn migrate sau.

Không nên tiếp tục mở rộng business logic phức tạp chỉ dựa vào `Post.Schedule*`.

## Thiết kế mục tiêu cho FR-U8.3

### Yêu cầu cốt lõi

AI phải có thể:

- nhận prompt tự nhiên của user;
- phân biệt đây là chat thường hay scheduling intent;
- thu thập đủ slot dữ liệu trước khi tạo job;
- tự liệt kê linked social accounts của user;
- tự liệt kê workspace social accounts;
- hỏi lại user nếu ambiguity chưa được giải;
- tại thời điểm chạy mới tra web;
- dựa trên dữ liệu web vừa lấy để tạo/assemble content;
- đăng content lên đúng target đã resolve.

### Slot mà agent phải resolve trước khi tạo job

Ít nhất agent phải có:

- `workspaceId`
- `intentType`
  - `fixed_content_schedule`
  - `agentic_live_content_schedule`
- `executeAt`
- `timezone`
- `platform`
- `targetSocialMediaId` hoặc danh sách target ids
- `contentSourceMode`
  - `existing_posts`
  - `create_at_runtime`
  - `assemble_from_web_data`
- `publishGoal`
- `confirmationState`

Agent chỉ được tạo schedule khi tất cả slot required đã đủ.

### Decision rules để chọn đúng social account

Agent phải tuân thủ rule rõ ràng:

1. Nếu user nói rõ social media id hoặc page/account name khớp duy nhất thì dùng luôn.
2. Nếu workspace chỉ có đúng 1 account phù hợp với platform yêu cầu thì có thể auto-resolve.
3. Nếu user có nhiều account cùng platform và chưa có default target, agent phải hỏi lại.
4. Nếu account tồn tại ở user-level nhưng chưa link vào workspace yêu cầu, agent phải hỏi:
   - link account đó vào workspace;
   - hoặc đổi workspace;
   - hoặc chọn account khác.
5. Nếu user chưa link account nào phù hợp thì agent phải báo thiếu điều kiện, không tự đoán.

### Example xử lý yêu cầu stakeholder nêu

Prompt:

> "Vào 5h chiều hãy tra kết quả xổ số miền bắc rồi đăng nó lên fb."

Agent phải làm như sau:

1. Xác định đây là `agentic_live_content_schedule`.
2. Resolve `5h chiều` theo timezone user hoặc hỏi timezone nếu chưa biết chắc.
3. Gọi tool lấy Facebook accounts đã link.
4. Nếu có nhiều Facebook page/account, hỏi user muốn page nào.
5. Khi đủ thông tin, tạo `PublishingSchedule` mode `agentic`.
6. Lưu prompt nghiệp vụ gốc, ví dụ:
   - "Tra kết quả xổ số miền bắc của ngày thực thi, viết caption ngắn gọn tiếng Việt, rồi đăng lên Facebook."
7. Lưu query template hoặc execution policy cho runtime.
8. Đến 17:00 local:
   - `n8n` chạy `web_search`;
   - trả kết quả về `Ai.Microservice`;
   - Gemini tạo content từ dữ liệu thực tế của ngày hôm đó;
   - tạo post nội bộ;
   - gọi `PublishPostsCommand`.

## Gemini conversational agent

### Kiến trúc đề xuất

Agent nên nằm trong `Ai.Microservice` vì:

- đã có `ChatSession` / `Chat`;
- đã có access tới post domain;
- đã có publish flow;
- đã có gRPC tới `User.Microservice`.

### SDK bắt buộc

Single-agent orchestration dùng:

- `Google.GenAI`
- `Microsoft.Extensions.AI`
- `Microsoft.Extensions.AI.Abstractions`

Pattern chuẩn cần áp dụng:

```csharp
var configuredModel = configuration["Gemini:ChatModel"]
    ?? throw new InvalidOperationException("Gemini:ChatModel is required.");

IChatClient chatClient = new Google.GenAI.Client(apiKey: apiKey)
    .AsIChatClient(configuredModel)
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();
```

`ChatOptions.Tools` phải chứa các tool business của hệ thống để Gemini có thể tự gọi function.

### Nguyên tắc hành vi của agent

System prompt của agent phải ép các rule sau:

- không tự đoán social account nếu có ambiguity;
- không tạo schedule nếu thiếu thời gian, timezone hoặc target platform/account;
- không publish ngay nếu user đang nói về future schedule;
- luôn hỏi lại user nếu chưa đủ dữ liệu;
- khi đã resolve target account thì phải lưu `targetSocialMediaId` cụ thể, không lưu kiểu mơ hồ như "facebook";
- với live-content jobs, chỉ dùng dữ liệu web lấy tại runtime;
- không tin hoàn toàn vào trí nhớ model cho dữ liệu thời sự;
- luôn ưu tiên tool `web_search` cho dữ liệu biến động theo ngày/giờ.

### Tương tác với chat model hiện tại

`Chat` hiện tại chưa đủ tốt cho agentic conversation vì thiếu:

- `Role`
- `ToolCallId`
- `ToolName`
- `ToolArgumentsJson`
- `ToolResultJson`
- `ModelName`
- `FinishReason`

Do đó cần một trong hai hướng:

1. mở rộng entity `Chat` hiện có;
2. hoặc thêm bảng mới như `AgentMessage`.

Khuyến nghị:

- giữ `ChatSession` làm session aggregate;
- thêm `AgentMessage` để lưu role-based transcript;
- không cố nhồi assistant/tool history vào một field `Config` JSON mơ hồ.

## Bộ tool/function cho agent

Đây là bộ tool tối thiểu để agent làm việc được thật.

### Read tools

- `get_user_workspaces`
- `get_workspace_social_accounts(workspaceId)`
- `get_linked_social_accounts(platform?)`
- `get_posts(workspaceId?, status?, currentChatOnly?, limit?)`
- `get_post(postId)`
- `get_schedules(workspaceId?, status?)`
- `get_schedule(scheduleId)`
- `get_user_timezone()`
- `get_current_time(timezone?)`

### Mutating tools

- `create_post(content, title?, hashtag?, postType?, platform?, socialMediaId?, resourceIds?, workspaceId?)`
- `update_post(postId, content?, title?, hashtag?, postType?, status?, socialMediaId?, resourceIds?, linkToCurrentChatSession?)`
- `create_schedule(...)`
- `update_schedule(scheduleId, ...)`
- `cancel_schedule(scheduleId)`
- `attach_items_to_schedule(scheduleId, items)`
- `link_social_to_workspace(workspaceId, socialMediaId)`

### Runtime execution tools

- `web_search(query, count, country, language, freshness?)`
- `create_runtime_post_from_search(scheduleId, searchPayload)`
- `publish_scheduled_content(scheduleId)`

### Tool responsibilities

#### `get_linked_social_accounts`

Trả về danh sách account social đã link của user, gồm:

- `socialMediaId`
- `platform`
- `displayName`
- `workspaceIds`
- `isDefault`
- `metadata summary` an toàn để hiển thị cho model

#### `get_workspace_social_accounts`

Tool này rất quan trọng vì user thường không muốn agent chọn account ngoài workspace hiện tại.

Nguồn dữ liệu hiện có:

- `GET /api/User/workspaces/{workspaceId}/social-medias`
- hoặc gRPC/internal query tương đương.

#### `web_search`

Đây không phải tool chạy trực tiếp trong model process.

Thay vào đó:

- agent gọi function business `web_search`;
- business function đó forward request sang `n8n`;
- `n8n` mới là nơi gọi Brave Search thật.

Điều này giúp:

- tách secret của Brave khỏi model runtime;
- audit được truy vấn;
- tái dùng cùng một tool cho chat thường lẫn scheduled runtime;
- có thể gắn rate limit, retry và sanitization ở `n8n`.

## Thiết kế workflow n8n

### Kết luận thiết kế

Không nên dùng `Schedule Trigger` làm engine cho từng user schedule động.

Lý do:

- `Schedule Trigger` phù hợp cho workflow có lịch cố định ở mức workflow;
- workflow phải được publish;
- timezone phụ thuộc workflow timezone hoặc instance timezone;
- không đẹp cho hàng nghìn schedule user-generated khác nhau.

Đối với FR-U8.3, pattern phù hợp hơn là:

- `Webhook` để nhận một execution request cụ thể;
- `Wait` node với mode `At Specified Time` để pause tới đúng thời điểm;
- sau đó gọi Brave Search;
- rồi callback về `Ai.Microservice`.

### Workflow đề xuất: 2 workflow tách biệt

#### Workflow A: `meai-scheduled-agent-job`

Mục đích:

- nhận job từ `Ai.Microservice`;
- đợi tới đúng giờ;
- gọi tool `web_search`;
- trả kết quả về `Ai.Microservice`.

Các node:

1. `Webhook`
   - path: `/meai/scheduled-agent-job`
   - method: `POST`
2. `Set`
   - normalize payload
3. `IF`
   - validate required fields
4. `Respond to Webhook`
   - trả `202 Accepted` ngay sau khi register job hợp lệ
5. `Wait`
   - mode: `At Specified Time`
   - value: `executeAtUtc`
6. `Execute Workflow`
   - gọi workflow B `meai-web-search`
7. `HTTP Request`
   - callback về `Ai.Microservice`

Lưu ý:

- do repo đang expose `n8n` dưới prefix `/n8n/`, public webhook path thực tế thường sẽ là `/n8n/webhook-test/meai/scheduled-agent-job`;
- không để response của webhook chờ đến khi `Wait` hoàn thành.

Payload vào workflow A:

```json
{
  "jobId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "scheduleId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "userId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "workspaceId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "executeAtUtc": "2026-04-23T10:00:00Z",
  "timezone": "Asia/Ho_Chi_Minh",
  "search": {
    "queryTemplate": "ket qua xo so mien bac ngay {{local_date_dd_MM_yyyy}}",
    "country": "VN",
    "searchLang": "vi",
    "count": 5
  },
  "callback": {
    "url": "http://api-gateway:8080/api/Ai/internal/agent-schedules/runtime-result",
    "token": "signed-internal-token"
  }
}
```

#### Workflow B: `meai-web-search`

Mục đích:

- là reusable tool wrapper cho Brave Search.

Các node:

1. `Execute Workflow Trigger` hoặc `Webhook`
2. `Set`
   - resolve query template thành query thực tế
3. `HTTP Request`
   - gọi Brave Search endpoint `/res/v1/web/search`
4. `Code` hoặc `Set`
   - rút gọn dữ liệu trả về
5. `Optional HTTP Request`
   - gọi thêm `/res/v1/llm/context` nếu cần summary context
6. `Return from Workflow`

Normalized output của workflow B:

```json
{
  "query": "ket qua xo so mien bac ngay 23/04/2026",
  "retrievedAtUtc": "2026-04-23T10:00:01Z",
  "results": [
    {
      "title": "Ket qua xo so mien Bac ngay 23/04/2026",
      "url": "https://example.com/...",
      "description": "...",
      "source": "example.com"
    }
  ],
  "llmContext": "...optional..."
}
```

### Tại sao dùng `Webhook + Wait` thay vì `Schedule Trigger`

Vì `Webhook + Wait`:

- phù hợp với job động do user tạo;
- mỗi execution mang payload riêng;
- `Wait` có thể pause execution cho tới một timestamp cụ thể;
- không cần tạo một workflow riêng cho từng schedule;
- dễ callback và audit hơn.

### Cấu hình Brave Search trong n8n

`HTTP Request` node phải gọi:

- `GET https://api.search.brave.com/res/v1/web/search`

Header:

- `Accept: application/json`
- `Accept-Encoding: gzip`
- `X-Subscription-Token: <BRAVE_SEARCH_API_KEY>`

Query params tối thiểu:

- `q`
- `count`
- `country`
- `search_lang`

Không để Gemini gọi Brave trực tiếp bằng raw HTTP nếu đã quyết định chuẩn hóa tool qua `n8n`.

## Contract giữa Ai.Microservice và n8n

### Register job

Khi agent tạo xong schedule mode `agentic`, `Ai.Microservice` phải:

1. persist `PublishingSchedule` vào DB trước;
2. gọi `n8n` workflow A qua webhook;
3. lưu `n8nExecutionId` hoặc correlation id;
4. chỉ chuyển job sang `waiting_for_execution` sau khi `n8n` ack thành công.

### Runtime callback

Đến giờ chạy, `n8n` callback về `Ai.Microservice` với:

- `jobId`
- `scheduleId`
- `query`
- `retrievedAtUtc`
- `search results`
- `execution metadata`

`Ai.Microservice` khi nhận callback phải:

1. kiểm tra chữ ký nội bộ hoặc bearer token;
2. kiểm tra job còn active;
3. kiểm tra idempotency;
4. dùng Gemini để tạo content từ `search results`;
5. tạo post nội bộ nếu cần;
6. gọi `PublishPostsCommand`.

## FR-U8.4 - Auto publish đúng thời điểm

### Fixed-content lane

Giữ nguyên hướng hiện tại:

- worker trong `Ai.Microservice` claim post tới hạn;
- gọi `PublishPostsCommand`;
- publish async qua `MassTransit` / `RabbitMQ`;
- `PublishToTargetConsumer` xử lý platform publish.

### Agentic lane

Không publish trực tiếp từ `n8n`.

Luồng chuẩn:

1. `n8n` tới giờ chạy -> lấy dữ liệu web.
2. `n8n` callback về `Ai.Microservice`.
3. `Ai.Microservice` mới tạo post/publish.

Lý do:

- publish logic, token usage, publication tracking đã nằm trong `.NET`;
- không nên tách logic publish social platforms sang `n8n`;
- giữ idempotency và audit tập trung trong microservice.

## Failure handling và idempotency

### Rule bắt buộc

- mọi schedule/job phải có `CorrelationId`;
- callback từ `n8n` phải idempotent;
- mỗi execution phải có `AttemptNumber`;
- nếu Brave Search fail tạm thời, có thể retry theo policy;
- nếu target account bị xóa/unlink/token hết hạn, job chuyển `needs_user_action`;
- nếu Gemini không tạo được content từ search result, job chuyển `failed` và phát notification;
- nếu publish fail ở một target nhưng multi-target mode là `publish_all`, cần giữ trạng thái per target;
- không được để cùng một schedule publish 2 lần do race condition.

### Source of truth

DB trong `Ai.Microservice` là source of truth cho:

- schedule status;
- target account;
- execution state;
- publish state.

`n8n` chỉ là orchestration/runtime executor, không phải source of truth nghiệp vụ.

## Quan hệ với chat trực tiếp Gemini

### Yêu cầu functional

User chat trực tiếp với Gemini phải dùng cùng agent core, nghĩa là:

- chat thường và schedule assistant dùng chung conversational engine;
- tool availability phụ thuộc context nhưng không tách thành 2 chatbot khác nhau;
- agent có thể trả lời bình thường hoặc chuyển sang create schedule flow khi nhận ra intent phù hợp.

### Endpoint hiện có

Chat agent hiện dùng endpoint:

- `POST /api/Ai/agent/sessions/{sessionId}/messages`

Endpoint này:

- nhận user message;
- load message history;
- build `IChatClient`;
- attach tools;
- stream hoặc return assistant response;
- persist transcript.

## Phạm vi tool/data mà agent được phép xem

Agent chỉ nên thấy dữ liệu tối thiểu cần thiết:

- workspace list của chính user;
- social accounts của chính user;
- posts của chính user;
- schedules của chính user;
- metadata tóm tắt an toàn, không dump raw secrets/token vào prompt.

Các field như `access_token`, `page_access_token`, refresh token, app secret:

- không được đưa vào prompt;
- không được trả về cho model;
- chỉ business layer nội bộ được dùng.

## Phased implementation được khuyến nghị

### Phase 1

- giữ nguyên single-post scheduling hiện tại;
- thêm docs và design này;
- thêm conversational Gemini agent skeleton dùng `Google.GenAI` + `IChatClient`;
- thêm read tools:
  - workspaces
  - linked social accounts
  - workspace social accounts
  - posts

### Phase 2

- thêm first-class `PublishingSchedule` aggregate;
- thêm APIs create/update/cancel/list schedules;
- migrate single-post schedule sang aggregate mới hoặc bridge song song.

#### Quyết định implementation cho phase 2

Để phù hợp với codebase hiện tại và giảm rủi ro regression cho publish pipeline đang chạy, phase 2 sẽ được implement theo cách sau:

- `PublishingSchedule` trở thành aggregate/API chính cho `fixed_content scheduling`;
- mode được hỗ trợ thực tế trong phase 2 là `fixed_content`;
- item type được hỗ trợ thực tế trong phase 2 là `post`;
- `video` và `agentic` schedule vẫn là mục tiêu phase sau;
- execution engine chưa bị thay mới ở phase 2;
- thay vào đó, khi tạo hoặc kích hoạt `PublishingSchedule`, hệ thống sẽ đồng bộ schedule xuống các field hiện có trên từng `Post`:
  - `ScheduleGroupId`
  - `ScheduledAtUtc`
  - `ScheduleTimezone`
  - `ScheduledSocialMediaIds`
  - `ScheduledIsPrivate`
- worker publish hiện có sẽ tiếp tục dispatch từ `posts` như cũ;
- `PublishingSchedule` đóng vai trò aggregate quản lý nhiều post trong cùng một lịch và là API chính để FE/agent thao tác.

### Phase 3

- thêm `n8n` workflow A và B;
- thêm `web_search` tool forwarding sang `n8n`;
- thêm internal callback endpoint;
- thêm runtime content creation từ search result.

#### Quyết định implementation cho phase 3

Để phase 3 có thể chạy end-to-end trên codebase hiện tại mà không thay publish engine, implementation sẽ theo các nguyên tắc sau:

- `PublishingSchedule` bắt đầu support thêm mode `agentic`;
- `agentic` schedule sẽ không cần item có sẵn tại thời điểm tạo;
- source of truth vẫn là DB trong `Ai.Microservice`;
- `n8n` chỉ làm:
  - đợi tới giờ;
  - gọi Brave Search;
  - callback search payload về `Ai.Microservice`;
- `Ai.Microservice` sẽ:
  - xác thực callback nội bộ;
  - kiểm tra idempotency bằng execution context trong schedule;
  - dùng Gemini để tạo runtime post content từ search result;
  - tạo `Post` nội bộ;
  - gắn post đó vào `PublishingSchedule` như runtime item;
  - gọi lại `PublishPostsCommand`;
- `web_search` tool cho Gemini chat thường cũng sẽ forward qua `n8n`, không gọi Brave trực tiếp;
- phase 3 cũng sẽ nới publish pipeline để hỗ trợ `text-only Facebook post`, vì nếu không thì use case kiểu "tra kết quả rồi đăng lên Facebook" sẽ fail ngay cả khi search và callback đã thành công.

### Phase 4

- thêm UI/FE cho schedule management;
- thêm schedule history, retry, pause/resume;
- thêm default target social account per workspace/platform;
- thêm notifications cho `needs_user_action`.

## Tình trạng implementation hiện tại

### Trạng thái tại ngày 2026-04-24

`Phase 1`, `Phase 2` và `Phase 3` đã được implement ở mức backend foundation + fixed-content scheduling + agentic runtime integration foundation. Sau cập nhật mới nhất, agent cũng đã có thể tạo/sửa draft post thật và link các post đó với chat session hiện tại.

### Đã hoàn thành trong phase 1

- đã giữ nguyên single-post scheduling hiện tại, không phá flow cũ;
- đã bổ sung endpoint chat agent trong `Ai.Microservice` để user chat trực tiếp với Gemini theo session;
- đã thêm application flow gửi message cho agent và đọc lại transcript trong session;
- đã dùng `Google.GenAI` + `Microsoft.Extensions.AI` với `IChatClient` và function invocation cho single-agent flow;
- đã thêm read tools tối thiểu cho agent:
  - lấy workspaces của user;
  - lấy linked social accounts của user;
  - lấy social accounts theo workspace;
  - lấy posts hiện có;
  - lấy posts của chat session hiện tại qua `get_posts(currentChatOnly: true)`;
  - lấy current time;
- đã thêm mutating tools cho post draft:
  - `create_post`;
  - `update_post`;
- đã thêm `chatSessionId` vào `Post`/`PostResponse` để bài được tạo trong hội thoại có thể quay lại đúng chat session;
- đã thêm API `GET /api/Ai/posts/chat-session/{chatSessionId}` để FE lấy danh sách post gắn với một hội thoại;
- đã mở rộng gRPC contract giữa `Ai.Microservice` và `User.Microservice` để agent tự resolve workspace và social account;
- đã lưu metadata hội thoại agent vào `Chat.Config` dưới dạng role-based metadata tạm thời để tránh migration sớm ở phase 1;
- đã có automated tests cho command/query chính của agent flow.

### Đã được bổ sung sau phase 1

- đã có tool tạo schedule thật (`create_schedule`);
- đã có tool tạo post thật (`create_post`);
- đã có tool sửa post thật (`update_post`);
- đã có first-class aggregate `PublishingSchedule`;
- đã có integration runtime với `n8n`;
- đã có `web_search` forwarding sang `n8n` và Brave Search runtime execution;
- đã có callback flow từ `n8n` về `Ai.Microservice` để hoàn tất live-content schedule.

### Ý nghĩa của trạng thái hiện tại

Sau phase 1, hệ thống đã có thể:

- cho user chat trực tiếp với Gemini trong `Ai.Microservice`;
- để agent tự hỏi ngược user khi thiếu thông tin;
- để agent đọc ngữ cảnh thật từ hệ thống thay vì đoán;
- tạo/sửa post draft thật từ hội thoại và link bài đó về chat session;
- tạo schedule thật, gọi `web_search`, và chạy nền cho agentic live-content schedule.

### Mục tiêu bàn giao của phase 2

Khi phase 2 hoàn thành, backend phải có tối thiểu:

- entity/repository/configuration cho `PublishingSchedule`, `PublishingScheduleItem`, `PublishingScheduleTarget`;
- API:
  - `POST /api/Ai/schedules`
  - `GET /api/Ai/schedules`
  - `GET /api/Ai/schedules/{scheduleId}`
  - `PUT /api/Ai/schedules/{scheduleId}`
  - `POST /api/Ai/schedules/{scheduleId}/cancel`
  - `POST /api/Ai/schedules/{scheduleId}/activate`
- validation để đảm bảo user chỉ schedule được các post thuộc workspace của chính mình;
- đồng bộ aggregate schedule xuống `Post.Schedule*` để reuse execution worker hiện tại;
- response model đủ cho FE/agent thấy:
  - metadata của schedule;
  - danh sách item;
  - danh sách target social accounts;
  - trạng thái schedule hiện tại.

### Đã hoàn thành trong phase 2

- đã thêm first-class aggregate backend:
  - `PublishingSchedule`
  - `PublishingScheduleItem`
  - `PublishingScheduleTarget`
- đã thêm EF Core migration cho aggregate mới;
- đã thêm APIs:
  - `POST /api/Ai/schedules`
  - `GET /api/Ai/schedules`
  - `GET /api/Ai/schedules/{scheduleId}`
  - `PUT /api/Ai/schedules/{scheduleId}`
  - `POST /api/Ai/schedules/{scheduleId}/cancel`
  - `POST /api/Ai/schedules/{scheduleId}/activate`
- đã thêm validation cho:
  - workspace ownership;
  - post ownership;
  - target social ownership;
  - schedule time phải ở tương lai;
  - timezone hợp lệ;
  - chỉ hỗ trợ `fixed_content` + `post` item ở phase này;
- đã bridge aggregate mới với execution pipeline cũ bằng cách đồng bộ xuống `Post.Schedule*`;
- đã nối runtime status tracking để schedule/item có thể chuyển qua các trạng thái như `scheduled`, `publishing`, `completed`, `failed`, `cancelled`;
- đã truyền `publishingScheduleId` qua command/message nội bộ để publish pipeline cập nhật lại item/schedule status sau khi dispatch;
- đã bổ sung automated tests cho create/cancel schedule và cập nhật test cho dispatch/publish flow.

### Giới hạn còn lại sau phase 2

- chưa support `video` item như first-class schedule item;
- các phần `agentic`, `create_schedule`, `web_search` qua `n8n`, và callback live-content đã được xử lý ở phase 3 bên dưới.

### Đã hoàn thành trong phase 3

- đã thêm mode `agentic` vào `PublishingSchedule`;
- đã thêm command handlers cho:
  - `CreateAgenticPublishingScheduleCommand`;
  - `UpdateAgenticPublishingScheduleCommand`;
  - `HandleAgentScheduleRuntimeResultCommand`;
- đã thêm `ExecutionContextJson` serializer/parser để giữ:
  - `n8n job id`;
  - `n8n execution id`;
  - `last processed callback job id`;
  - `runtime post id`;
  - search payload gần nhất;
- đã thêm `IN8nWorkflowClient` và implementation gọi:
  - webhook đăng ký job đợi tới giờ;
  - webhook `web_search`;
- đã thêm internal callback endpoint:
  - `POST /api/Ai/internal/agent-schedules/runtime-result`;
- đã thêm runtime content generation service để Gemini tạo caption/post draft từ search result;
- đã nối callback runtime về flow:
  - tạo `Post` nội bộ;
  - gắn vào `PublishingSchedule` như runtime item;
  - gọi `PublishPostsCommand`;
- đã mở rộng Gemini tool catalog để agent có thể:
  - `get_schedules`;
  - `get_schedule`;
  - `create_schedule`;
  - `web_search`;
- đã nới publish pipeline để hỗ trợ `text-only Facebook post`, phục vụ use case kiểu "đến giờ tra cứu dữ liệu rồi đăng status Facebook";
- đã thêm `n8n` workflow artifacts trong repo tại:
  - `Backend/Compose/n8n-local/workflows/meai-scheduled-agent-job.json`;
  - `Backend/Compose/n8n-local/workflows/meai-web-search.json`;
- đã thêm hướng dẫn import/cấu hình workflow tại:
  - `Backend/Compose/n8n-local/README.md`;
- đã thêm cấu hình compose cho `Ai.Microservice` và `n8n` để local stack có thể truyền `N8n__*` options và `BRAVE_SEARCH_API_KEY`;
- đã chuyển `n8n` local/prod-like compose sang auto-import workflow JSON qua service `n8n-import`, rồi auto-activate workflow bằng CLI trước khi `n8n` start;
- lý do cần bước activate riêng: `n8n import:workflow` có thể import workflow về trạng thái inactive dù JSON có `active: true`; nếu không activate lại, production webhook `/webhook/...` sẽ trả `404`.

## Hướng dẫn FE tích hợp API

### Trạng thái backend hiện tại

Ở thời điểm hiện tại:

- `fixed_content scheduling` đã hoạt động ở mức API và execution runtime;
- auto-dispatch tới publish pipeline đã hoạt động;
- `agentic scheduling` đã có API và runtime foundation;
- kết quả publish thực tế còn phụ thuộc platform account, token, media hợp lệ và external provider.

Frontend nên coi đây là 2 flow khác nhau:

- `fixed_content`: user chọn post có sẵn rồi schedule;
- `agentic`: user nhập yêu cầu để hệ thống tới giờ mới đi lấy dữ liệu và tạo runtime post.

### API FE cần dùng

#### 1. Tạo post draft trước khi schedule hoặc từ chat

- `POST /api/Ai/posts`
- mục đích:
  - tạo post draft trước;
  - tạo post draft từ agent/chat khi user nói "tạo bài này";
  - lưu caption, media, post type;
  - link post vào chat session nếu request có `chatSessionId`;
  - sau đó mới đưa `postId` vào schedule.

Request body hiện tại:

- `workspaceId`: optional nếu có `chatSessionId`; backend sẽ lấy workspace từ chat session.
- `chatSessionId`: optional, dùng để gắn post với hội thoại.
- `socialMediaId`: optional, nếu đã resolve target account.
- `title`: optional.
- `content`: optional `PostContent`.
- `status`: optional, thường là `draft`.
- `postBuilderId`: optional, dùng cho flow post builder; nếu trùng thì backend upsert để tránh duplicate.
- `platform`: optional, ví dụ `facebook`, `instagram`, `threads`, `tiktok`.

```json
{
  "workspaceId": "<workspaceId>",
  "chatSessionId": "<chatSessionId>",
  "title": "Test scheduled post",
  "content": {
    "content": "Caption",
    "hashtag": "#tag",
    "resourceList": ["<resourceId>"],
    "postType": "posts"
  },
  "status": "draft",
  "platform": "facebook"
}
```

Response success là `Result<PostResponse>`. `PostResponse` hiện có thêm `chatSessionId`:

```json
{
  "value": {
    "id": "<postId>",
    "userId": "<userId>",
    "username": "user",
    "avatarUrl": null,
    "workspaceId": "<workspaceId>",
    "chatSessionId": "<chatSessionId>",
    "socialMediaId": null,
    "title": "Test scheduled post",
    "content": {
      "content": "Caption",
      "hashtag": "#tag",
      "resourceList": ["<resourceId>"],
      "postType": "posts"
    },
    "status": "draft",
    "schedule": null,
    "isPublished": false,
    "media": [],
    "publications": [],
    "createdAt": "2026-04-24T00:00:00Z",
    "updatedAt": "2026-04-24T00:00:00Z"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

Lưu ý FE:

- với TikTok, resource phải là đúng `1` video;
- với TikTok, không nên để `postType` rỗng;
- với Facebook text-only post, `resourceList` có thể rỗng;
- `post.status = draft` ở đây chỉ có nghĩa là bài viết chưa publish.
- nếu truyền cả `workspaceId` và `chatSessionId`, chúng phải cùng workspace, nếu không backend trả `Post.ChatSessionWorkspaceMismatch`.
- nếu chỉ truyền `chatSessionId`, backend tự resolve `workspaceId` từ chat session.

#### 1.1. Sửa post draft đã tạo

- `PUT /api/Ai/posts/{postId}`
- mục đích:
  - sửa caption/title/media/status của post;
  - gắn hoặc cập nhật link với chat session qua `chatSessionId`;
  - phục vụ flow agent khi user nói "sửa bài vừa tạo".

Request body là partial update; field `null` được hiểu là không đổi:

```json
{
  "workspaceId": "<workspaceId>",
  "chatSessionId": "<chatSessionId>",
  "socialMediaId": "<socialMediaId>",
  "title": "Updated title",
  "content": {
    "content": "Updated caption",
    "hashtag": "#updated",
    "resourceList": ["<resourceId>"],
    "postType": "posts"
  },
  "status": "draft"
}
```

Response success là `Result<PostResponse>`.

Validation và ownership:

- chỉ owner của post được sửa;
- `chatSessionId` phải thuộc cùng user;
- nếu post đã có `workspaceId`, chat session mới phải cùng workspace;
- nếu post đã được schedule, backend giữ `status = scheduled`.

#### 1.2. Lấy post theo chat session

- `GET /api/Ai/posts/chat-session/{chatSessionId}`
- mục đích:
  - render các draft/post được tạo trong hội thoại hiện tại;
  - giúp FE hiển thị "bài đã tạo từ chat này";
  - giúp agent tìm lại bài để sửa khi user nói "sửa bài vừa tạo".

Query params:

- `cursorCreatedAt`: optional, dùng cho cursor pagination.
- `cursorId`: optional, dùng cùng `cursorCreatedAt`.
- `limit`: optional, default `20`, max `50`.

Ví dụ:

```bash
curl -X GET "http://localhost:2406/api/Ai/posts/chat-session/<chatSessionId>?limit=20" \
  -H "Authorization: Bearer <access-token>"
```

Response success là `Result<IEnumerable<PostResponse>>`.

Error cases chính:

- `ChatSession.NotFound`: chat session không tồn tại hoặc đã xóa.
- `ChatSession.Unauthorized`: chat session không thuộc current user.

#### 1.3. Agent tool tương ứng với post APIs

Gemini agent hiện có các tool post sau:

- `get_posts(workspaceId?, status?, currentChatOnly?, limit?)`
- `create_post(content, title?, hashtag?, postType?, platform?, socialMediaId?, resourceIds?, workspaceId?)`
- `update_post(postId, content?, title?, hashtag?, postType?, status?, socialMediaId?, resourceIds?, linkToCurrentChatSession?)`

Mapping behavior:

- `create_post` luôn tạo `status = draft` và link với chat session hiện tại.
- `update_post` mặc định `linkToCurrentChatSession = true`, nên bài được sửa từ chat sẽ được gắn lại với chat session hiện tại.
- `get_posts(currentChatOnly: true)` chỉ trả các post có `chatSessionId` là session đang chat.

#### 2. Tạo fixed-content schedule

- `POST /api/Ai/schedules`
- dùng khi user đã chọn sẵn một hoặc nhiều post để đăng vào thời điểm cố định.

Payload mẫu:

```json
{
  "workspaceId": "<workspaceId>",
  "name": "Daily publish",
  "mode": "fixed_content",
  "executeAtUtc": "2026-04-23T15:30:00Z",
  "timezone": "Asia/Ho_Chi_Minh",
  "isPrivate": false,
  "items": [
    {
      "itemType": "post",
      "itemId": "<postId>",
      "sortOrder": 1,
      "executionBehavior": "publish_all"
    }
  ],
  "targets": [
    {
      "socialMediaId": "<socialMediaId>",
      "isPrimary": true
    }
  ]
}
```

FE nên lưu các field quan trọng từ response:

- `scheduleId`
- `status`
- `items[].status`
- `targets[]`
- `errorCode`
- `errorMessage`

#### 3. Tạo agentic schedule

- `POST /api/Ai/schedules`
- dùng khi user không chọn post có sẵn mà muốn hệ thống tới giờ mới search/generate nội dung.

Payload mẫu:

```json
{
  "workspaceId": "<workspaceId>",
  "name": "Lottery runtime",
  "mode": "agentic",
  "executeAtUtc": "2026-04-23T16:00:00Z",
  "timezone": "Asia/Ho_Chi_Minh",
  "isPrivate": false,
  "platformPreference": "facebook",
  "agentPrompt": "Vào đúng giờ hãy tra kết quả xổ số miền bắc hôm nay rồi đăng lên Facebook.",
  "search": {
    "queryTemplate": "kết quả xổ số miền bắc hôm nay",
    "count": 5,
    "country": "VN",
    "searchLanguage": "vi",
    "freshness": "pd"
  },
  "targets": [
    {
      "socialMediaId": "<socialMediaId>",
      "isPrimary": true
    }
  ]
}
```

Lưu ý FE:

- `agentic` schedule không cần `items` tại thời điểm tạo;
- runtime post sẽ được backend tạo sau khi callback từ `n8n` thành công;
- FE không nên tự tạo placeholder post cho flow này trừ khi product muốn có UI preview riêng.

#### 4. Đọc danh sách schedule

- `GET /api/Ai/schedules`
- query hỗ trợ:
  - `workspaceId`
  - `status`
  - `limit`

Mục đích FE:

- render danh sách lịch đăng;
- filter theo workspace;
- poll để cập nhật runtime status gần thời điểm publish.

#### 5. Đọc chi tiết một schedule

- `GET /api/Ai/schedules/{scheduleId}`

Mục đích FE:

- xem chi tiết item, target, error;
- hiển thị timeline/trạng thái runtime;
- nếu fail thì lấy `errorCode` và `errorMessage` để hiển thị cho user.

#### 6. Update / Cancel / Activate

- `PUT /api/Ai/schedules/{scheduleId}`
- `POST /api/Ai/schedules/{scheduleId}/cancel`
- `POST /api/Ai/schedules/{scheduleId}/activate`

FE nên dùng:

- `cancel` khi user muốn dừng lịch còn chưa chạy xong;
- `activate` khi user muốn bật lại lịch đã cancel hoặc draft-like flow hợp lệ;
- `update` khi sửa giờ, target, prompt hoặc item list.

#### 7. Chat trực tiếp với Gemini

- tạo chat session:
  - `POST /api/Ai/chat-sessions`
- gửi message cho agent:
  - `POST /api/Ai/agent/sessions/{sessionId}/messages`
- đọc transcript:
  - `GET /api/Ai/agent/sessions/{sessionId}/messages`
- đọc các post được tạo/sửa trong chat session:
  - `GET /api/Ai/posts/chat-session/{sessionId}`

FE có thể dùng flow này khi muốn cho user nói tự nhiên thay vì điền form schedule thủ công.

Payload tạo chat session:

```json
{
  "workspaceId": "<workspaceId>",
  "sessionName": "Chiến dịch Facebook tuần này"
}
```

Payload gửi message:

```json
{
  "message": "Tạo một bài Facebook ngắn về chương trình khuyến mãi hôm nay."
}
```

Khi assistant dùng `create_post`, FE nên gọi `GET /api/Ai/posts/chat-session/{sessionId}` hoặc đọc `postId` trong nội dung/tool result để hiển thị draft mới cho user chỉnh sửa tiếp.

#### 7.1. Contract action metadata cho frontend

Response của agent không chỉ có text. `assistantMessage` hiện có thêm field `actions`, và transcript từ `GET /api/Ai/agent/sessions/{sessionId}/messages` cũng trả lại field này cho từng message.

FE không nên parse `assistantMessage.content` để đoán agent vừa làm gì. Hãy render UI action cards từ `assistantMessage.actions`.

Shape của một action:

```json
{
  "type": "post_create",
  "toolName": "create_post",
  "status": "completed",
  "entityType": "post",
  "entityId": "<postId>",
  "label": "Tiêu đề bài viết",
  "summary": "Draft post created and linked to the current chat session.",
  "occurredAt": "2026-04-24T00:10:00Z"
}
```

Field semantics:

- `type`: loại hành động cấp sản phẩm, ví dụ `tool_call`, `post_create`, `post_update`, `schedule_create`, `web_search`.
- `toolName`: tên tool agent đã gọi, ví dụ `get_posts`, `create_post`, `update_post`, `create_schedule`, `web_search`.
- `status`: `completed` hoặc `failed`.
- `entityType`: loại entity bị tác động, ví dụ `post`, `schedule`, `workspace`, `chat_session`.
- `entityId`: id entity để FE route/link tới detail page.
- `label`: text ngắn để render card title, ví dụ post title hoặc schedule name.
- `summary`: mô tả ngắn backend cung cấp, không cần parse từ assistant text.
- `occurredAt`: thời điểm backend ghi nhận action.

Ví dụ response `POST /api/Ai/agent/sessions/{sessionId}/messages` khi agent tạo draft post:

```json
{
  "value": {
    "sessionId": "<sessionId>",
    "userMessage": {
      "id": "<userMessageId>",
      "sessionId": "<sessionId>",
      "role": "user",
      "content": "tạo một bài mới tên chúc mừng bạn Thắng ngọt đã trúng",
      "toolNames": [],
      "actions": []
    },
    "assistantMessage": {
      "id": "<assistantMessageId>",
      "sessionId": "<sessionId>",
      "role": "assistant",
      "content": "Tôi đã tạo thành công bài đăng mới dưới dạng bản nháp...",
      "model": "gemini-3.1-flash-lite-preview",
      "toolNames": ["create_post"],
      "actions": [
        {
          "type": "post_create",
          "toolName": "create_post",
          "status": "completed",
          "entityType": "post",
          "entityId": "<postId>",
          "label": "chúc mừng bạn Thắng ngọt đã trúng",
          "summary": "Draft post created and linked to the current chat session.",
          "occurredAt": "2026-04-24T00:10:00Z"
        }
      ]
    }
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

FE rendering rules:

- Nếu `actions[].status = completed` và `entityType = post`, hiển thị card "Đã tạo/sửa bài viết" với CTA mở post detail bằng `entityId`.
- Nếu `type = schedule_create`, hiển thị card lịch đăng với CTA mở schedule detail.
- Nếu `type = web_search`, hiển thị trạng thái đã search web; không cần tự parse link trong text.
- Nếu `status = failed`, hiển thị error/action failed card từ `summary`.
- `toolNames` vẫn dùng được để hiển thị chip/debug, nhưng UI nghiệp vụ nên dựa vào `actions`.
- `content` vẫn là text hội thoại để hiển thị bubble chat.

### State machine FE nên hiểu

#### Schedule status

FE nên map trạng thái như sau:

- `scheduled`
  - lịch đã được tạo và đang chờ tới giờ chạy.
- `waiting_for_execution`
  - áp dụng chủ yếu cho `agentic`; job đã đăng ký với `n8n`.
- `executing`
  - backend đang xử lý runtime logic.
- `publishing`
  - đã đi vào publish pipeline.
- `completed`
  - tất cả item đã publish thành công.
- `failed`
  - có lỗi trong runtime hoặc publish.
- `needs_user_action`
  - thiếu token/account/scope hoặc cần user xử lý thủ công.
- `cancelled`
  - user đã hủy lịch.

#### Item status

- `scheduled`
- `publishing`
- `published`
- `failed`
- `cancelled`

FE nên ưu tiên hiển thị `item.status` và `schedule.status` cùng lúc, vì:

- một schedule có thể fail do chỉ một item fail;
- response hiện tại đã chứa per-item error bậc cơ bản.

### Hướng dẫn FE xử lý theo thời gian

#### Với fixed-content schedule

1. user tạo hoặc chọn post draft
2. user chọn target social account
3. FE gọi `POST /api/Ai/schedules`
4. FE redirect sang màn hình detail hoặc refresh list
5. gần thời điểm chạy, FE poll `GET /api/Ai/schedules/{id}`
6. khi status đổi sang `completed` hoặc `failed`, FE dừng poll

#### Với agentic schedule

1. user chọn workspace, target, giờ chạy
2. user nhập prompt business
3. FE gọi `POST /api/Ai/schedules` với `mode = agentic`
4. FE đợi trạng thái `waiting_for_execution`
5. gần thời điểm chạy, FE poll `GET /api/Ai/schedules/{id}`
6. backend tự:
   - gọi `n8n`
   - search web
   - generate runtime post
   - publish
7. FE hiển thị `completed` hoặc `failed`

### Hướng dẫn FE xử lý lỗi

Các trường cần đọc:

- `schedule.errorCode`
- `schedule.errorMessage`
- `items[].errorMessage`

Các tình huống FE cần giải thích rõ cho user:

- `failed` vì token social hết hạn;
- `failed` vì media không đúng với platform;
- `failed` vì provider reject request;
- `needs_user_action` vì account bị unlink hoặc thiếu scope.

Với TikTok, FE nên validate trước khi cho schedule:

- phải có đúng `1` video resource;
- không dùng text-only post;
- không dùng nhiều media;
- nên đặt `postType` rõ ràng cho video post.

### Khuyến nghị UX cho frontend

- khi tạo schedule thành công, luôn lưu `scheduleId` để chuyển sang màn detail;
- ở list view, hiển thị tối thiểu:
  - `name`
  - `mode`
  - `executeAtUtc`
  - `status`
  - target platform
- ở detail view, hiển thị thêm:
  - item list
  - target list
  - error message
  - `lastExecutionAt`
- với `agentic`, hiển thị thêm:
  - `agentPrompt`
  - search summary nếu có trong `executionContextJson`

### Chưa nằm trong phase 3

- chưa support `video` runtime item cho `agentic` schedule;
- chưa có retry/pause/resume first-class cho `agentic` schedule ngoài trạng thái nền tảng;
- chưa có notification flow cho `needs_user_action`;
- chưa có end-to-end integration test chạy thật qua `n8n` container + Brave Search sandbox;
- chưa có policy layer sâu hơn cho việc chọn default social account khi user prompt mơ hồ.

## Kết luận thiết kế

Để đáp ứng đúng mong muốn đã nêu, `FR-U8` được triển khai như sự kết hợp của 3 năng lực:

1. `Scheduling domain` để lưu ý định đăng bài một cách first-class.
2. `Gemini agent` để nói chuyện trực tiếp với user, hỏi ngược và gọi tools.
3. `n8n + Brave Search` để thực thi các runtime web-grounded jobs đúng thời điểm.

Sau phase 3, repo đã có đủ backend foundation cho toàn bộ trục này:

- `fixed_content scheduling` bằng aggregate riêng;
- `agentic schedule` có callback runtime;
- Gemini tool calling để đọc ngữ cảnh, tạo schedule và gọi `web_search`;
- `n8n` workflow artifacts để chờ đến giờ rồi lấy dữ liệu web;
- callback runtime về `Ai.Microservice`;
- publish pipeline đủ để chạy use case text-only Facebook post.
- khi `n8n` hoặc web search không reachable, `Ai.Microservice` hiện fail-fast bằng timeout ngắn và trả error code rõ (`N8n.Unreachable`, `N8n.WebSearchTimeout`, `N8n.RegisterTimeout`) thay vì treo request quá lâu.

Phần còn lại chủ yếu là hardening và productization:

- `video`/multi-format runtime generation;
- retry, pause/resume, notifications;
- rule/policy tốt hơn cho account resolution;
- e2e deployment/test thực chiến với `n8n` và Brave Search secret thật.

## Tài liệu tham chiếu đã xác minh

- Google GenAI .NET SDK: `https://github.com/googleapis/dotnet-genai`
- Microsoft `IChatClient`: `https://learn.microsoft.com/en-us/dotnet/ai/ichatclient`
- n8n Webhook node: `https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-base.webhook/`
- n8n Wait node: `https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-base.wait/`
- n8n Schedule Trigger node: `https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-base.scheduletrigger/`
- n8n HTTP Request node: `https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-base.httprequest/`
- Brave Search API: `https://brave.com/search/api/`
