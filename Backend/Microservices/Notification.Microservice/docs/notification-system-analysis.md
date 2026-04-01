# Phân tích hệ thống Notification

## Mục tiêu
Tài liệu này tổng hợp cách hệ thống notification hiện đang hoạt động trong `Notification.Microservice`, các điểm tích hợp với những service khác, và những rủi ro/lỗi tiềm ẩn được suy ra trực tiếp từ mã nguồn.

## 1. Tổng quan kiến trúc

`Notification.Microservice` được thiết kế theo hướng event-driven:

1. Service khác publish `NotificationRequestedEvent`
2. Notification service consume event qua MassTransit/RabbitMQ
3. Service lưu notification vào Postgres
4. Nếu user đang online thì gửi realtime qua SignalR
5. Client có thể gọi API để lấy danh sách notification hoặc đánh dấu đã đọc

Các điểm vào chính của hệ thống:
- Khởi tạo service và map controller/hub: `Backend/Microservices/Notification.Microservice/src/WebApi/Program.cs:10-67`
- Consumer nhận event: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Consumers/NotificationRequestedConsumer.cs:8-30`
- API đọc/mark-as-read: `Backend/Microservices/Notification.Microservice/src/WebApi/Controllers/NotificationsController.cs:13-102`
- SignalR hub: `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs:8-66`

## 2. Luồng hoạt động chi tiết

### 2.1. Service khác tạo event notification
Notification được publish bằng shared contract `NotificationRequestedEvent`:
- Event contract: `Backend/Microservices/SharedLibrary/Contracts/Notifications/NotificationRequestedEvent.cs:3-19`
- Factory tiện ích: `Backend/Microservices/SharedLibrary/Contracts/Notifications/NotificationRequestedEventFactory.cs:5-27`
- Các loại notification đã khai báo: `Backend/Microservices/SharedLibrary/Contracts/Notifications/NotificationTypes.cs:3-16`

Ví dụ các nơi đang publish notification:
- AI image submitted: `Backend/Microservices/Ai.Microservice/src/Application/Chats/Commands/CreateChatImageCommand.cs:123-156`
- AI image completed: `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Consumers/ImageStatusConsumers.cs:56-71`
- Subscription activated/renewed: `Backend/Microservices/User.Microservice/src/Application/Subscriptions/Commands/PurchaseSubscriptionCommand.cs:215-239`
- Confirm payment subscription: `Backend/Microservices/User.Microservice/src/Application/Subscriptions/Commands/ConfirmSubscriptionPaymentCommand.cs:161-182`

### 2.2. Notification service consume event
`NotificationRequestedConsumer` chỉ log metadata cơ bản rồi chuyển xử lý cho `NotificationDispatchService`:
- Consumer: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Consumers/NotificationRequestedConsumer.cs:21-29`
- Đăng ký MassTransit/RabbitMQ: `Backend/Microservices/Notification.Microservice/src/Infrastructure/DependencyInjection.cs:39-62`

### 2.3. NotificationDispatchService là trung tâm của luồng xử lý
`DispatchAsync(...)` hiện chạy theo thứ tự sau:

1. Bỏ qua event không có recipient
2. Chuẩn hóa `NotificationId`
3. Kiểm tra notification đã tồn tại hay chưa
4. Tạo entity `Notification`
5. Kiểm tra online state của từng recipient qua Redis presence
6. Tạo `UserNotification` cho từng recipient
7. Save toàn bộ xuống database
8. Gửi realtime qua SignalR cho user được đánh giá là online

Code chính: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:37-123`

## 3. Mô hình dữ liệu

Hệ thống dùng 2 bảng:

### 3.1. Bảng `notifications`
Lưu nội dung notification dùng chung cho mọi recipient:
- Entity: `Backend/Microservices/Notification.Microservice/src/Domain/Entities/Notification.cs:6-27`

### 3.2. Bảng `user_notifications`
Lưu trạng thái theo từng user:
- `IsRead`
- `ReadAt`
- `WasOnlineWhenCreated`
- `CreatedAt`
- `UpdatedAt`

Entity: `Backend/Microservices/Notification.Microservice/src/Domain/Entities/UserNotification.cs:6-29`

Migration ban đầu xác nhận:
- quan hệ FK từ `user_notifications` sang `notifications`
- index theo user/read state
- unique key `(notification_id, user_id)`

Tham chiếu: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Migrations/20260401051102_InitialNotificationSchema.cs:12-78`

## 4. Realtime delivery

### 4.1. SignalR hub
Hub route là `/hubs/notifications`, method client nhận là `NotificationReceived`:
- Hub: `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs:8-66`
- Map hub: `Backend/Microservices/Notification.Microservice/src/WebApi/Setups/SignalRSetup.cs:47-49`

### 4.2. Cơ chế gửi realtime
Sau khi persist xong, `NotificationDispatchService` tạo `NotificationDeliveryModel` và dùng `INotificationRealtimeNotifier` để gửi cho group của user:
- Delivery model: `Backend/Microservices/Notification.Microservice/src/Application/Notifications/Models/NotificationDeliveryModel.cs:3-16`
- SignalR notifier: `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/SignalRNotificationRealtimeNotifier.cs:7-21`
- Group naming theo user: `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs:33-35`, `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs:60`

### 4.3. Presence tracking bằng Redis
Presence được lưu bằng 2 loại key:
- `notifications:presence:user:{userId}`
- `notifications:presence:connection:{connectionId}`

Code: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/RedisNotificationPresenceService.cs:16-55`

SignalR Redis backplane được cấu hình ở: `Backend/Microservices/Notification.Microservice/src/WebApi/Setups/SignalRSetup.cs:11-45`

## 5. API hiện có

Notification service hiện expose các API sau:

- `GET /api/Notification/notifications`
- `PATCH /api/Notification/notifications/{userNotificationId}/read`
- `PATCH /api/Notification/notifications/read-all`

Controller: `Backend/Microservices/Notification.Microservice/src/WebApi/Controllers/NotificationsController.cs:23-95`

Các handler liên quan:
- Lấy danh sách notification: `Backend/Microservices/Notification.Microservice/src/Application/Notifications/Queries/GetUserNotificationsQuery.cs:22-66`
- Mark một notification là read: `Backend/Microservices/Notification.Microservice/src/Application/Notifications/Commands/MarkNotificationAsReadCommand.cs:24-67`
- Mark tất cả notification là read: `Backend/Microservices/Notification.Microservice/src/Application/Notifications/Commands/MarkAllNotificationsAsReadCommand.cs:22-58`
- Repository đọc dữ liệu: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Repositories/UserNotificationRepository.cs:39-80`

## 6. Authentication / Authorization

REST endpoint dùng custom `[Authorize]`:
- Attribute: `Backend/Microservices/SharedLibrary/Attributes/AuthorizeAttribute.cs:8-52`
- Controller áp dụng: `Backend/Microservices/Notification.Microservice/src/WebApi/Controllers/NotificationsController.cs:13-16`

Hub không dùng attribute authorize mà tự kiểm tra claim `NameIdentifier` trong `OnConnectedAsync()`:
- Hub auth check: `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs:24-35`

JWT middleware lấy token từ:
- query string `access_token` cho hub
- cookie `access_token`
- bearer token trong header

Tham chiếu: `Backend/Microservices/SharedLibrary/Middleware/JwtMiddleware.cs:16-59`

## 7. Các rủi ro / lỗi tiềm ẩn

### 7.1. Notification service chưa được wire mặc định trong dev compose / gateway
Mặc dù notification microservice đã nằm trong solution: `Backend/Microservices/Microservices.sln:36-46`, nhưng `docker-compose.yml` hiện chỉ thấy `ai-microservice`, `user-microservice`, `api-gateway`, Redis, RabbitMQ, Postgres; chưa thấy `notification-microservice`: `Backend/Compose/docker-compose.yml:1-170`

API Gateway mặc định cũng chỉ add route cho `User` và `Ai`: `Backend/Microservices/ApiGateway/src/Setups/YarpRuntimeSetup.cs:97-128`

Gateway có hỗ trợ thêm service động qua env config, nhưng cần cấu hình riêng: `Backend/Microservices/ApiGateway/src/Setups/YarpRuntimeSetup.cs:130-169`

### 7.2. Redis nằm trên critical path của việc persist notification
`DispatchAsync()` gọi `IsUserOnlineAsync()` trước khi tạo `UserNotification` và trước khi save DB: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:75-96`

`IsUserOnlineAsync()` gọi Redis trực tiếp, không có fallback: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/RedisNotificationPresenceService.cs:46-49`

Hệ quả: nếu Redis fail, notification có thể không được lưu xuống DB.

### 7.3. Cấu hình Redis password có dấu hiệu không đồng nhất
`EnvironmentConfig` gán default password Redis là `default` khi thiếu config: `Backend/Microservices/SharedLibrary/Configs/EnvironmentConfig.cs:65-67`

`DependencyInjection` dùng giá trị đó để tạo `ConnectionMultiplexer`: `Backend/Microservices/Notification.Microservice/src/Infrastructure/DependencyInjection.cs:23-37`

Nhưng SignalR setup chỉ set password nếu cấu hình thực sự có giá trị: `Backend/Microservices/Notification.Microservice/src/WebApi/Setups/SignalRSetup.cs:19-41`

Điều này có thể dẫn đến tình huống SignalR backplane kết nối được nhưng presence Redis không kết nối được.

### 7.4. Presence TTL 6 giờ nhưng không có refresh
Presence expiry hiện là 6 giờ: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/RedisNotificationPresenceService.cs:8`

TTL chỉ được set lúc connect: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/RedisNotificationPresenceService.cs:16-24`

Không thấy heartbeat/refresh TTL trong hub: `Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs:24-57`

Hệ quả: connection sống quá lâu có thể vẫn còn hoạt động nhưng Redis presence đã hết hạn, dẫn tới false offline và không push realtime.

### 7.5. Save DB xong rồi mới push realtime
Hiện tại notification được save trước, sau đó mới gửi SignalR: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:95-115`

Nếu save DB thành công nhưng SignalR throw exception:
- notification vẫn tồn tại trong DB
- retry có thể bị chặn bởi check notification đã tồn tại: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:49-56`
- kết quả là mất realtime push cho event đó

### 7.6. Một recipient lỗi có thể chặn recipient phía sau
Vòng lặp gửi realtime chạy tuần tự và không có `try/catch` riêng từng user: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:98-115`

Nếu một lần gửi bị lỗi, các user online còn lại phía sau có thể không nhận được push.

### 7.7. Race condition khi cùng xử lý duplicate event
Service đang kiểm tra tồn tại bằng `GetByIdAsync()` trước khi insert: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:49-56`

Repository đọc notification: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Repositories/NotificationRepository.cs:22-27`

Dù DB có PK ở `notifications.id`: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Migrations/20260401051102_InitialNotificationSchema.cs:14-30`, vẫn có khả năng 2 consumer cùng pass bước check rồi một bên fail khi insert.

### 7.8. `WasOnlineWhenCreated` không phản ánh đúng semantics tên field
Field được set từ trạng thái online tại lúc dispatch, không phải chắc chắn tại lúc event được tạo: `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:58-60`, `Backend/Microservices/Notification.Microservice/src/Infrastructure/Logic/Services/NotificationDispatchService.cs:75-91`

Trong khi event có `CreatedAt` từ upstream: `Backend/Microservices/SharedLibrary/Contracts/Notifications/NotificationRequestedEvent.cs:15-19`

Nếu queue delay, field này dễ bị hiểu sai nghĩa.

### 7.9. Mark-as-read có dấu hiệu save thừa
Hai command handler gọi save trực tiếp:
- Single read: `Backend/Microservices/Notification.Microservice/src/Application/Notifications/Commands/MarkNotificationAsReadCommand.cs:56-64`
- Read all: `Backend/Microservices/Notification.Microservice/src/Application/Notifications/Commands/MarkAllNotificationsAsReadCommand.cs:44-55`

Nhưng `UnitOfWorkBehavior` cũng auto-save cho command thành công: `Backend/Microservices/Notification.Microservice/src/Application/Behaviors/UnitOfWorkBehavior.cs:21-36`

Điều này có thể gây save hai lần.

### 7.10. Test coverage runtime còn mỏng
Test project hiện chủ yếu là architecture test: `Backend/Microservices/Notification.Microservice/test/ArchitectureTest.cs:13-91`

Chưa thấy test rõ ràng cho:
- dispatch flow
- Redis presence
- SignalR delivery
- retry / idempotency
- controller + handler runtime

Ngoài ra test `Controller_Should_have_DependencyOnMediatR` đang scan assembly `Infrastructure`, không phải `WebApi`: `Backend/Microservices/Notification.Microservice/test/ArchitectureTest.cs:78-91`

## 8. Đánh giá tổng thể

### Điểm tốt
- Hướng thiết kế event-driven hợp lý
- Postgres là source of truth, nên offline user vẫn lấy lại notification được
- Tách `notifications` và `user_notifications` phù hợp với mô hình fan-out

### Điểm cần lưu ý
- Wiring môi trường chạy thực tế có vẻ chưa hoàn chỉnh mặc định
- Redis đang ảnh hưởng cả durability thay vì chỉ ảnh hưởng realtime
- Realtime delivery chưa đủ resilient trước lỗi từng recipient hoặc lỗi sau khi DB đã commit
- Presence model hiện tại có thể sai sau thời gian kết nối dài

## 9. Ghi chú xác minh

Đã thử chạy test của `Notification.Microservice` trong môi trường hiện tại nhưng không thực hiện được vì máy chạy không có `dotnet` trong PATH. Vì vậy tài liệu này là kết quả phân tích tĩnh dựa trên mã nguồn hiện có.
