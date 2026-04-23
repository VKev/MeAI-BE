# FR-U8 Scheduling and Auto Posting

## Phạm vi

Tài liệu này mô tả phần đã triển khai cho hai functional requirement:

- `FR-U8.1`: hệ thống cho phép người dùng tạo lịch chỉ định thời điểm nội dung sẽ được đăng.
- `FR-U8.4`: hệ thống tự động publish nội dung đã lên lịch khi tới thời điểm được chỉ định.

Phần triển khai nằm trong `Ai.Microservice`, tận dụng lại luồng publish bất đồng bộ hiện có qua MassTransit/RabbitMQ và `PublishToTargetConsumer`.

## FR-U8.1 - Tạo lịch đăng nội dung

### API mới

Đã thêm endpoint:

```http
POST /api/Ai/posts/{postId}/schedule
```

Endpoint yêu cầu user đã đăng nhập giống các API post hiện có. User chỉ được schedule post thuộc chính user đó.

Request body:

```json
{
  "scheduleGroupId": null,
  "scheduledAtUtc": "2026-04-24T03:00:00Z",
  "timezone": "Asia/Bangkok",
  "socialMediaIds": [
    "11111111-1111-1111-1111-111111111111"
  ],
  "isPrivate": false
}
```

Ý nghĩa field:

- `scheduledAtUtc`: thời điểm publish theo UTC. Nếu input có local kind/offset, command sẽ normalize về UTC trước khi lưu.
- `timezone`: timezone người dùng chọn, lưu để frontend hiển thị lại lịch theo ngữ cảnh của user.
- `socialMediaIds`: danh sách tài khoản mạng xã hội sẽ publish khi tới giờ.
- `isPrivate`: tùy chọn privacy dùng cho TikTok publish, được truyền lại vào publish flow.
- `scheduleGroupId`: optional. Nếu không truyền, backend tự sinh `Guid.CreateVersion7()`. Nếu post đã có schedule, endpoint có thể được gọi lại để reschedule và giữ group hiện tại khi không truyền group mới.

Response vẫn dùng envelope hiện có:

```json
{
  "value": {
    "id": "...",
    "status": "scheduled",
    "schedule": {
      "scheduleGroupId": "...",
      "scheduledAtUtc": "2026-04-24T03:00:00Z",
      "timezone": "Asia/Bangkok",
      "socialMediaIds": ["..."],
      "isPrivate": false
    }
  },
  "isSuccess": true,
  "isFailure": false,
  "error": null
}
```

### Application layer

Đã thêm `SchedulePostCommand` và `SchedulePostCommandHandler`.

Handler thực hiện các validation chính:

- Post phải tồn tại, chưa bị delete, và thuộc user hiện tại.
- Post phải có `WorkspaceId`, vì publish flow hiện tại yêu cầu workspace.
- `scheduledAtUtc` phải nằm trong tương lai.
- `socialMediaIds` phải có ít nhất một GUID hợp lệ.
- Post type chỉ cho phép nhóm publish hiện tại hỗ trợ: `posts`, `reels` và các alias tương ứng (`post`, `reel`, `video`).
- Post chưa được publish hoặc đang publish. Các publication `failed` cũ không chặn việc schedule lại.
- Social media account phải tồn tại, thuộc user, và thuộc các platform publish đang hỗ trợ: TikTok, Facebook, Instagram, Threads.

Khi hợp lệ, handler lưu các field schedule vào post:

- `ScheduleGroupId`
- `ScheduledAtUtc`
- `ScheduleTimezone`
- `ScheduledSocialMediaIds`
- `ScheduledIsPrivate`
- `Status = "scheduled"`
- `UpdatedAt`

`PostResponseBuilder` và `PostMapping` đã map các field này thành `PostScheduleResponse`, nên các API GET post/workspace/post builder tiếp tục trả shape response hiện có và bổ sung `schedule` khi post có lịch.

### Persistence

Đã map thêm các field schedule trong `PostConfiguration`:

- `schedule_group_id`
- `scheduled_at_utc`
- `scheduled_social_media_ids`
- `scheduled_is_private`
- `schedule_timezone`

Đã thêm index:

```text
ix_posts_status_scheduled_at_utc(status, scheduled_at_utc)
```

Index này phục vụ worker tìm các post `status = scheduled` đã tới hạn.

Migration đã tạo:

```text
Backend/Microservices/Ai.Microservice/src/Infrastructure/Migrations/20260423080829_AddPostScheduling.cs
```

### Guard khi update/publish

`UpdatePostCommand` đã được bổ sung guard: nếu post đang có schedule metadata thì status luôn được giữ là `scheduled`. Điều này tránh trường hợp frontend gửi partial/full update có `status = draft` và vô tình làm lịch không được worker pick.

`PublishPostsCommand` đã clear schedule metadata khi user publish thủ công. Nhờ đó một post đã publish trước giờ hẹn sẽ không bị worker auto-post lại sau đó.

## FR-U8.4 - Tự động publish khi tới lịch

### Background worker

Đã thêm hosted service:

```text
Infrastructure.Logic.Services.ScheduledPostPublishingWorker
```

Worker chạy trong `Ai.Microservice`, poll mỗi 15 giây. Mỗi vòng chạy gọi:

```text
ScheduledPostDispatchService.DispatchDuePostsAsync()
```

Dispatcher lấy tối đa 20 post đến hạn mỗi batch rồi gọi lại `PublishPostsCommand` với target lấy từ schedule đã lưu.

### Claim lịch đến hạn

Đã thêm repository method:

```csharp
Task<IReadOnlyList<ScheduledPostDispatchCandidate>> ClaimDueScheduledPostsAsync(
    DateTime dueBeforeUtc,
    int limit,
    CancellationToken cancellationToken);
```

Method này dùng SQL `FOR UPDATE SKIP LOCKED` để claim atomically các post đến hạn:

- Chỉ lấy post `deleted_at IS NULL`.
- Chỉ lấy post `status = 'scheduled'`.
- Chỉ lấy post có `workspace_id`, `schedule_group_id`, `scheduled_at_utc`.
- Chỉ lấy post có `scheduled_social_media_ids` không rỗng.
- Chỉ lấy post có `scheduled_at_utc <= now`.

Khi claim thành công, DB update ngay:

- `status = 'processing'`
- clear các schedule fields
- update `updated_at`

Sau đó repository trả về `ScheduledPostDispatchCandidate` gồm:

- `PostId`
- `UserId`
- `SocialMediaIds`
- `IsPrivate`

Cách này giúp tránh việc nhiều instance worker cùng publish một lịch.

### Publish flow tự động

Sau khi claim, dispatcher gọi:

```csharp
new PublishPostsCommand(
    scheduledPost.UserId,
    [new PublishPostTargetInput(
        scheduledPost.PostId,
        scheduledPost.SocialMediaIds,
        scheduledPost.IsPrivate)])
```

Từ đây hệ thống dùng lại publish flow hiện có:

- `PublishPostsCommand` validate post/social media.
- Tạo `post_publications` placeholder trạng thái `processing`.
- Publish `PublishToTargetRequested` message qua `IBus`.
- `PublishToTargetConsumer` thực hiện publish tới Facebook/Instagram/TikTok/Threads.
- Consumer cập nhật publication thành `published` hoặc `failed`.
- Consumer phát notification completion/failure như luồng publish thủ công hiện tại.

Nếu `PublishPostsCommand` fail trước khi gửi message, dispatcher gọi:

```csharp
MarkScheduledDispatchFailedAsync(postId)
```

Post sẽ chuyển sang `failed` để worker không retry vô hạn cùng một lịch lỗi.

### Dependency injection

Đã đăng ký:

```csharp
services.AddScoped<ScheduledPostDispatchService>();
services.AddHostedService<ScheduledPostPublishingWorker>();
```

Worker được khởi động cùng `Ai.Microservice`.

## Test đã thêm/cập nhật

Đã thêm test cho `FR-U8.1`:

- `SchedulePostCommandTests.Handle_ShouldPersistScheduleAndReturnScheduledPost`
- `SchedulePostCommandTests.Handle_ShouldFailWhenScheduledTimeIsInPast`

Đã thêm test cho `FR-U8.4`:

- `ScheduledPostDispatchServiceTests.DispatchDuePostsAsync_ShouldSendPublishCommandForClaimedPosts`
- `ScheduledPostDispatchServiceTests.DispatchDuePostsAsync_ShouldMarkPostFailedWhenPublishCommandFails`

Đã cập nhật test cũ:

- `PublishPostsCommandTests` được chỉnh theo publish flow bất đồng bộ hiện tại: command tạo placeholder và publish `PublishToTargetRequested`, không gọi trực tiếp platform publish services.
- `GenerateSocialMediaCaptionsCommandTests` được bổ sung mock billing/pricing dependency hiện tại.

Kết quả kiểm thử:

```text
dotnet test Backend/Microservices/Ai.Microservice/test/test.csproj
Passed: 41/41
```

Lưu ý: test/build hiện có warning security từ package `System.Security.Cryptography.Xml` trong `Infrastructure.csproj`, nhưng không có test failure.
