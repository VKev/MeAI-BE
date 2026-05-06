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
  - dùng RAG grounding từ social account đã chọn;
  - sinh content mới phù hợp với bối cảnh thời điểm đó;
  - tạo post runtime;
  - publish post đến tất cả targets của schedule.

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
- backend có thể rewrite prompt nhẹ để làm rõ future intent nếu kết quả thực tế sẽ chỉ được biết ở runtime;
- backend persist:
  - `AgentPrompt`
  - `MaxContentLength`
  - stored search query template
  - full target list
  - `executeAtUtc`
  - `timezone`
- backend tự suy ra `PlatformPreference` từ target `primary`, hoặc target đầu tiên nếu không có `primary`;
- backend register n8n/runtime job;
- backend trả về response có:
  - `action = "future_ai_schedule_created"`
  - `scheduleId`

Không tạo draft post trước. Không cần preview là source of truth.

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
7. Generate runtime post draft từ:
   - `AgentPrompt`
   - fresh web search
   - recommendation summary
   - page profile grounding
8. Enforce `MaxContentLength` như hard cap trên `PostContent.Content`.
9. Tạo runtime `Post`.
10. Publish runtime `Post` tới toàn bộ targets của schedule.

## RAG và search

FR-U8 runtime phải kết hợp cả hai lớp grounding:

- `web search`: để lấy context mới tại thời điểm thực thi;
- `RAG microservice`: để lấy page profile, voice, pattern nội dung, và past-post references của account.

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
- `ExecutionContextJson` cho state runtime, n8n, và debug

Không cần có `PublishingScheduleItem` ở thời điểm create. Item sẽ được tạo khi runtime thực sự sinh ra post.

## Legacy lane

`POST /api/Ai/schedules` mode `fixed_content` vẫn tồn tại cho use case legacy hoặc manual scheduling.

Nhưng đối với FR-U8 product flow, lane chính là:

- chat tạo `agentic` schedule trước;
- AI runtime tạo content sau.
