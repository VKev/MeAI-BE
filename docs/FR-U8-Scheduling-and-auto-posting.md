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
  - nếu có target `TikTok` thì `desiredPostType` của runtime phải bị ép sang `reels`;
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
7. Dùng Kie tool-calling loop thật để model có thể gọi:
   - `web_search`
   - `fetch_url`
   - `import_media`
   - `create_runtime_post_draft`
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

- `TikTok`:
  - chỉ publish lane video;
  - runtime `postType` phải là `reels`;
  - draft phải có đúng 1 video resource;
  - nếu draft là text-only, image-only, hoặc nhiều media thì schedule phải fail sớm trước khi vào publish consumer.
- `Facebook reels`:
  - `postType = reels`;
  - phải có đúng 1 video.
- `Facebook posts`:
  - có thể là text-only;
  - nếu có media thì không được mix image + video;
  - không được nhiều video trong cùng một publish.
- `Instagram reels`:
  - `postType = reels`;
  - phải có đúng 1 video.
- `Instagram posts`:
  - hiện tại chỉ hỗ trợ đúng 1 media item;
  - media đó có thể là image hoặc video tùy publish lane hiện tại.
- `Threads`:
  - hỗ trợ text-only hoặc đúng 1 media item;
  - runtime không được tạo nhiều media cho một draft Threads.

Điểm quan trọng của FR-U8 là các rule này phải được encode ngay trong agentic runtime lane, không chờ đến bước `PublishToTargetConsumer` mới phát hiện mismatch.

## RAG và search

FR-U8 runtime phải kết hợp cả hai lớp grounding:

- `web search`: để lấy context mới tại thời điểm thực thi;
- `RAG microservice`: để lấy page profile, voice, pattern nội dung, và past-post references của account.

Provider/model/runtime hiện tại:

- AI provider cho runtime lane: **Kie**
- Endpoint: `/codex/v1/responses`
- Default model: `gpt-5-4`
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

Nhưng đối với FR-U8 product flow, lane chính là:

- chat tạo `agentic` schedule trước;
- AI runtime tạo content sau.
