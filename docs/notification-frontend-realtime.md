# Tài liệu hợp nhất Notification System

## Mục tiêu
Tài liệu này hợp nhất nội dung từ:
- phân tích kiến trúc và luồng xử lý của `Notification.Microservice`
- hướng dẫn frontend tích hợp realtime qua API Gateway

Mục tiêu là cung cấp một nguồn tham chiếu duy nhất cho cả backend, gateway và frontend khi triển khai, kiểm thử, vận hành và debug hệ thống notification.

---

## 1. Tổng quan kiến trúc

`Notification.Microservice` được thiết kế theo hướng event-driven:

1. Service khác publish `NotificationRequestedEvent`
2. Notification service consume event qua MassTransit/RabbitMQ
3. Service lưu notification vào Postgres
4. Nếu user đang online thì gửi realtime qua SignalR
5. Client gọi REST API để lấy danh sách notification hoặc đánh dấu đã đọc

Các điểm vào chính:
- Consumer nhận event
- `NotificationDispatchService` xử lý fan-out, persist và realtime delivery
- REST API cho lấy danh sách / đánh dấu đã đọc
- SignalR Hub cho realtime push
- API Gateway expose route ra ngoài frontend

---

## 2. Luồng end-to-end

### 2.1. Luồng tổng thể

1. Một service upstream tạo `NotificationRequestedEvent`
2. Event đi qua RabbitMQ / MassTransit đến Notification service
3. Notification service tạo bản ghi `Notification`
4. Service tạo `UserNotification` cho từng recipient
5. Dữ liệu được lưu xuống Postgres
6. Nếu user đang online, service gửi `NotificationReceived` qua SignalR
7. Frontend đang kết nối hub sẽ nhận notification realtime
8. Frontend vẫn có thể đồng bộ lại qua REST API để tránh miss dữ liệu khi reconnect hoặc mất kết nối

### 2.2. Luồng frontend nên triển khai

1. User login và lấy access token theo flow hiện tại của hệ thống
2. Frontend gọi REST API để lấy dữ liệu ban đầu
3. Frontend mở SignalR connection tới gateway
4. Khi nhận event `NotificationReceived`, append hoặc update local state ngay
5. Khi user đọc notification, frontend gọi API mark-as-read để đồng bộ với server
6. Khi reconnect, frontend gọi lại API danh sách để đồng bộ phần có thể bị miss trong lúc mất kết nối

Khuyến nghị:
- Dùng `userNotificationId` để dedupe trong store
- Parse `payloadJson` an toàn trước khi render
- Hiển thị trạng thái kết nối realtime để dễ debug

---

## 3. Event contract và notification types

Notification được publish bằng shared contract `NotificationRequestedEvent`.

Factory `NotificationRequestedEventFactory.CreateForUser(...)` sẽ:
- serialize `payload` thành `PayloadJson`
- tự sinh `NotificationId`
- set `RecipientUserIds = [userId]`
- dùng `DateTime.UtcNow` nếu không truyền `createdAt`

Hiện tại hệ thống đã có nhiều call site publish từ AI microservice và User microservice cho các nhóm notification như:
- `AiImageGenerationSubmitted`
- `AiVideoGenerationSubmitted`
- `AiVideoExtensionSubmitted`
- `AiImageGenerationCompleted`
- `AiImageGenerationFailed`
- `AiVideoGenerationCompleted`
- `AiVideoGenerationFailed`
- `UserSubscriptionActivated`
- `UserSubscriptionRenewed`

---

## 4. Mô hình dữ liệu

Hệ thống dùng 2 bảng:

### 4.1. `notifications`
Lưu nội dung notification dùng chung cho mọi recipient.

### 4.2. `user_notifications`
Lưu trạng thái theo từng user:
- `isRead`
- `readAt`
- `wasOnlineWhenCreated`
- `createdAt`
- `updatedAt`

Đặc điểm dữ liệu:
- có FK từ `user_notifications` sang `notifications`
- có index theo user và trạng thái read
- có unique key `(notification_id, user_id)`

Thiết kế này phù hợp với mô hình fan-out: một notification có thể phát cho nhiều recipient, trong khi trạng thái đọc vẫn được quản lý riêng từng user.

---

## 5. Realtime delivery

### 5.1. Hub route
Hub route chính là:
- `/hubs/notifications`

Route tương thích thêm qua gateway:
- `/api/Notification/hubs/notifications`

Client event nhận được là:
- `NotificationReceived`

### 5.2. Cơ chế gửi realtime
Sau khi persist xong, `NotificationDispatchService` tạo `NotificationDeliveryModel` và gửi qua `INotificationRealtimeNotifier` tới group theo user.

Payload dùng chung cho cả REST và SignalR có dạng:

```ts
export type NotificationDeliveryModel = {
  notificationId: string;
  userNotificationId: string;
  userId: string;
  source: string;
  type: string;
  title: string;
  message: string;
  payloadJson: string | null;
  createdByUserId: string | null;
  isRead: boolean;
  readAt: string | null;
  wasOnlineWhenCreated: boolean;
  createdAt: string;
  updatedAt: string | null;
};
```

Lưu ý:
- `payloadJson` hiện chỉ được backend lưu và forward nguyên trạng
- backend notification không parse typed payload ở tầng này
- frontend nên parse an toàn trước khi sử dụng

Ví dụ parse an toàn:

```ts
export function parseNotificationPayload(payloadJson: string | null) {
  if (!payloadJson) return null;

  try {
    return JSON.parse(payloadJson);
  } catch {
    return null;
  }
}
```

### 5.3. Presence tracking
Presence hiện dùng Redis với 2 loại key:
- `notifications:presence:user:{userId}`
- `notifications:presence:connection:{connectionId}`

SignalR Redis backplane cũng được cấu hình riêng.

---

## 6. API Gateway và REST API

### 6.1. Gateway routes
API Gateway expose các route notification:
- `/hubs/notifications`
- `/api/Notification/hubs/notifications`
- `/api/Notification/notifications`

### 6.2. REST API hiện có
- `GET /api/Notification/notifications?source={Creator|Social}` -> lấy danh sách notification (hỗ trợ filter theo source)
- `PATCH /api/Notification/notifications/{userNotificationId}/read` -> đánh dấu một notification đã đọc
- `PATCH /api/Notification/notifications/read-all` -> đánh dấu tất cả đã đọc

Ví dụ frontend nên dùng qua gateway:
- `GET /api/Notification/notifications?onlyUnread=false&limit=50&source=Creator`
- `PATCH /api/Notification/notifications/{userNotificationId}/read`
- `PATCH /api/Notification/notifications/read-all`

Trong local docker compose hiện tại, base URL gateway là:

```txt
http://localhost:2406
```

---

## 7. Authentication / Authorization

### 7.1. REST API
REST endpoint dùng custom `[Authorize]`.

### 7.2. SignalR hub
Hub không dùng attribute authorize mà tự kiểm tra claim `NameIdentifier` trong `OnConnectedAsync()`.

### 7.3. JWT sources
JWT middleware hiện lấy token từ:
- query string `access_token` cho hub
- cookie `access_token`
- bearer token trong header

Hệ quả cho frontend:
- nếu dùng bearer token, truyền token qua `accessTokenFactory`
- nếu dùng cookie auth, vẫn nên giữ `credentials: "include"` cho REST call

---

## 8. Hướng dẫn frontend tích hợp realtime

### 8.1. Cài package

```bash
npm install @microsoft/signalr
```

### 8.2. Tạo connection

```ts
import * as signalR from "@microsoft/signalr";

export type NotificationDeliveryModel = {
  notificationId: string;
  userNotificationId: string;
  userId: string;
  type: string;
  title: string;
  message: string;
  payloadJson: string | null;
  createdByUserId: string | null;
  isRead: boolean;
  readAt: string | null;
  wasOnlineWhenCreated: boolean;
  createdAt: string;
  updatedAt: string | null;
};

export function createNotificationConnection(baseUrl: string, accessToken: string) {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${baseUrl}/hubs/notifications`, {
      accessTokenFactory: () => accessToken,
      withCredentials: true,
      transport:
        signalR.HttpTransportType.WebSockets |
        signalR.HttpTransportType.ServerSentEvents |
        signalR.HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Information)
    .build();
}
```

### 8.3. Hook mẫu

```tsx
import { useEffect, useMemo, useState } from "react";
import { createNotificationConnection, type NotificationDeliveryModel } from "./notification-signalr";

async function fetchNotifications(baseUrl: string, accessToken: string, source?: string) {
  let url = `${baseUrl}/api/Notification/notifications?onlyUnread=false&limit=50`;
  if (source) {
    url += `&source=${source}`;
  }

  const response = await fetch(url, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
    credentials: "include",
  });

  if (!response.ok) {
    throw new Error("Cannot load notifications");
  }

  const result = await response.json();
  return (result.value ?? []) as NotificationDeliveryModel[];
}

export function useNotifications(baseUrl: string, accessToken: string, source?: string) {
  const [items, setItems] = useState<NotificationDeliveryModel[]>([]);
  const [isConnected, setIsConnected] = useState(false);

  const connection = useMemo(() => {
    if (!accessToken) return null;
    return createNotificationConnection(baseUrl, accessToken);
  }, [baseUrl, accessToken]);

  useEffect(() => {
    if (!accessToken || !connection) return;

    let disposed = false;

    const upsert = (notification: NotificationDeliveryModel) => {
      // Nếu có truyền filter source, ta kiểm tra và bỏ qua các notification không thuộc source này
      if (source && notification.source !== source) return;

      setItems((current) => {
        const existingIndex = current.findIndex(
          (item) => item.userNotificationId === notification.userNotificationId,
        );

        if (existingIndex === -1) {
          return [notification, ...current];
        }

        const next = [...current];
        next[existingIndex] = notification;
        return next;
      });
    };

    connection.on("NotificationReceived", upsert);
    connection.onreconnected(async () => {
      setIsConnected(true);
      const latest = await fetchNotifications(baseUrl, accessToken, source);
      if (!disposed) setItems(latest);
    });
    connection.onclose(() => setIsConnected(false));

    (async () => {
      const initial = await fetchNotifications(baseUrl, accessToken, source);
      if (!disposed) setItems(initial);

      await connection.start();
      if (!disposed) setIsConnected(true);
    })().catch((error) => {
      console.error("Notification realtime connection failed", error);
      setIsConnected(false);
    });

    return () => {
      disposed = true;
      connection.off("NotificationReceived", upsert);
      void connection.stop();
    };
  }, [accessToken, baseUrl, connection]);

  return {
    items,
    isConnected,
    unreadCount: items.filter((item) => !item.isRead).length,
  };
}
```

### 8.4. Mark as read

```ts
export async function markNotificationAsRead(
  baseUrl: string,
  accessToken: string,
  userNotificationId: string,
) {
  const response = await fetch(
    `${baseUrl}/api/Notification/notifications/${userNotificationId}/read`,
    {
      method: "PATCH",
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
      credentials: "include",
    },
  );

  if (!response.ok) {
    throw new Error("Cannot mark notification as read");
  }

  return response.json();
}
```

---

## 9. Checklist kiểm thử nhanh

1. Login thành công
2. `GET /api/Notification/notifications` qua gateway trả về `200`
3. Mở SignalR connection tới `/hubs/notifications` qua gateway
4. Publish một notification cho user đó
5. Frontend nhận `NotificationReceived` mà không cần refresh trang
6. `PATCH /api/Notification/notifications/{userNotificationId}/read` thành công
7. Sau reconnect, frontend refetch được danh sách mới nhất

---

## 10. Rủi ro và điểm cần lưu ý

### 10.1. Wiring môi trường dev / gateway có thể chưa hoàn chỉnh
Tài liệu phân tích cho thấy notification service có trong solution, nhưng `docker-compose.yml` hiện chưa thấy service này được wire mặc định. Gateway cũng có dấu hiệu chỉ add route mặc định cho `User` và `Ai`, còn notification cần cấu hình thêm.

### 10.2. Redis nằm trên critical path trước khi save DB
`DispatchAsync()` kiểm tra online state qua Redis trước khi tạo `UserNotification` và trước khi save DB. Nếu Redis lỗi, notification có thể không được persist, tức Redis đang ảnh hưởng cả durability chứ không chỉ realtime.

### 10.3. Redis password có thể cấu hình không đồng nhất
Presence Redis và SignalR backplane có logic set password khác nhau. Điều này có thể gây tình huống một phần kết nối được, phần còn lại lỗi.

### 10.4. Presence TTL 6 giờ nhưng không có refresh
TTL chỉ set lúc connect. Nếu connection sống quá lâu mà không refresh TTL, user có thể vẫn online thực tế nhưng bị đánh giá offline trong Redis.

### 10.5. Save DB xong mới push realtime
Nếu save DB thành công nhưng SignalR gửi lỗi, notification vẫn có trong DB nhưng realtime event có thể bị mất cho lần đó.

### 10.6. Một recipient lỗi có thể chặn recipient khác
Realtime loop gửi tuần tự và không có `try/catch` riêng từng user. Một lỗi ở giữa có thể chặn user online phía sau.

### 10.7. Duplicate event có thể gặp race condition
Service đang check tồn tại trước khi insert. Trong tình huống concurrent consumer, vẫn có khả năng cả hai cùng qua bước check và một bên fail khi insert.

### 10.8. `WasOnlineWhenCreated` có thể gây hiểu sai semantics
Field này phản ánh trạng thái online tại thời điểm dispatch, không chắc là tại thời điểm event được tạo từ upstream.

### 10.9. Mark-as-read có thể đang save hai lần
Command handler gọi save trực tiếp, trong khi pipeline behavior cũng có auto-save cho command thành công.

### 10.10. Test coverage runtime còn mỏng
Hiện chủ yếu thấy architecture test, chưa thấy test runtime đủ dày cho dispatch flow, Redis presence, SignalR delivery, retry, idempotency và controller flow.

---

## 11. Khuyến nghị triển khai thực tế

### 11.1. Cho frontend
- Luôn lấy initial state bằng REST trước khi mở hub
- Dedupe theo `userNotificationId`
- Re-fetch sau reconnect
- Parse `payloadJson` an toàn
- Hiển thị `isConnected` để hỗ trợ debug
- Ưu tiên đi qua gateway thay vì gọi trực tiếp notification service

### 11.2. Cho backend / hạ tầng
- Wire `notification-microservice` vào compose và gateway mặc định nếu muốn chạy đầy đủ ở local/dev
- Tách Redis khỏi critical path persist nếu mục tiêu là đảm bảo durability
- Bọc send realtime theo từng recipient để tránh lỗi dây chuyền
- Xem xét cơ chế retry hoặc outbox cho realtime delivery sau khi DB commit
- Bổ sung heartbeat / refresh TTL cho presence
- Rà soát logic save dư ở mark-as-read
- Tăng test coverage cho flow runtime quan trọng

---

## 12. Kết luận

Hệ thống notification hiện có nền tảng kiến trúc hợp lý:
- event-driven
- Postgres làm source of truth
- fan-out qua `user_notifications`
- hỗ trợ realtime bằng SignalR

Ở phía frontend, flow tích hợp qua gateway tương đối rõ ràng và có thể triển khai ổn định nếu kết hợp REST + SignalR đúng cách.

Tuy vậy, để hệ thống chạy bền vững trong môi trường thực tế, cần đặc biệt lưu ý các rủi ro về wiring môi trường, presence Redis, độ resilient của realtime delivery và khả năng đồng bộ sau reconnect.
