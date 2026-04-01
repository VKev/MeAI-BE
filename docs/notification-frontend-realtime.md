# Frontend realtime integration cho Notification Microservice

Tài liệu này mô tả cách frontend nhận notification realtime thông qua API Gateway và đồng bộ với REST API của `Notification.Microservice`.

## 1. Endpoint nên dùng

### Realtime hub qua gateway
- Khuyến nghị: `/hubs/notifications`
- Tương thích thêm: `/api/Notification/hubs/notifications`

### REST API qua gateway
- Lấy danh sách notification: `GET /api/Notification/notifications?onlyUnread=false&limit=50`
- Đánh dấu 1 notification đã đọc: `PATCH /api/Notification/notifications/{userNotificationId}/read`
- Đánh dấu tất cả đã đọc: `PATCH /api/Notification/notifications/read-all`

Trong môi trường local với docker compose hiện tại, base URL của gateway là:

```txt
http://localhost:2406
```

## 2. Luồng frontend nên triển khai

1. User login và lấy access token như flow hiện tại của hệ thống.
2. Frontend gọi REST API để lấy dữ liệu ban đầu.
3. Frontend mở SignalR connection tới gateway.
4. Khi nhận event `NotificationReceived`, append/update state local ngay.
5. Khi user đọc notification, gọi REST API mark-as-read để đồng bộ server.

Khuyến nghị:
- Dùng `UserNotificationId` để dedupe notification trong store.
- Parse `PayloadJson` trước khi render nếu cần dữ liệu động.
- Sau khi reconnect, nên gọi lại `GET /api/Notification/notifications` để đồng bộ phần bị miss trong lúc mất kết nối.

## 3. Payload frontend sẽ nhận

Server sẽ push event `NotificationReceived` với payload có shape tương đương:

```ts
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
```

Nếu `payloadJson` khác `null`, frontend nên parse an toàn:

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

## 4. Cài package

```bash
npm install @microsoft/signalr
```

## 5. Ví dụ triển khai với React

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

### Hook mẫu

```tsx
import { useEffect, useMemo, useState } from "react";
import { createNotificationConnection, type NotificationDeliveryModel } from "./notification-signalr";

async function fetchNotifications(baseUrl: string, accessToken: string) {
  const response = await fetch(`${baseUrl}/api/Notification/notifications?onlyUnread=false&limit=50`, {
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

export function useNotifications(baseUrl: string, accessToken: string) {
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
      const latest = await fetchNotifications(baseUrl, accessToken);
      if (!disposed) setItems(latest);
    });
    connection.onclose(() => setIsConnected(false));

    (async () => {
      const initial = await fetchNotifications(baseUrl, accessToken);
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

## 6. Ví dụ đánh dấu đã đọc

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

## 7. Lưu ý để chạy ổn định

- Frontend nên ưu tiên kết nối qua gateway, không gọi trực tiếp notification service.
- Nếu frontend dùng bearer token, hãy truyền token qua `accessTokenFactory`.
- Nếu frontend đang dùng cookie auth, vẫn nên giữ `credentials: "include"` cho các REST call.
- Khi deploy production, đổi `http://localhost:2406` thành domain gateway thật và dùng `https/wss`.
- Nên hiển thị trạng thái kết nối realtime (`isConnected`) để dễ debug.

## 8. Checklist test nhanh

1. Login thành công.
2. Gọi `GET /api/Notification/notifications` qua gateway trả về `200`.
3. Mở SignalR connection tới `/hubs/notifications` qua gateway.
4. Publish 1 notification cho user đó.
5. Frontend nhận event `NotificationReceived` mà không cần refresh trang.
6. Gọi `PATCH /api/Notification/notifications/{userNotificationId}/read` thành công.
