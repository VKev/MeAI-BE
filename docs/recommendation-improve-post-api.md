# Tích hợp FE: AI Improve Post Realtime

Tài liệu này hướng dẫn FE tích hợp flow AI improve post theo realtime notification. Không dùng interval polling cho trạng thái improve.

Backend tạo `RecommendPost` để lưu suggestion. Post gốc chỉ đổi khi user bấm approve.

## Nguyên tắc tích hợp

- `POST /improve` chỉ dùng để submit task.
- FE nhận trạng thái task qua Notification service + SignalR.
- FE không poll `GET /improve` mỗi vài giây.
- `GET /improve` chỉ dùng cho manual refresh hoặc recovery khi user mở lại tab và đã miss realtime event.
- Notification payload có đủ dữ liệu để FE cập nhật UI trực tiếp.

## Endpoint

| Method | Path | FE dùng để |
|---|---|---|
| `POST` | `/api/Ai/recommendations/posts/{postId}/improve` | Submit task improve. |
| `GET` | `/api/Ai/recommendations/posts/{postId}/improve` | Recovery/manual refresh, không dùng interval polling. |
| `POST` | `/api/Ai/recommendations/posts/{postId}/improve/approve` | Apply suggestion đã completed vào post gốc. |
| `POST` | `/api/Ai/recommendations/posts/{postId}/improve/reject` | Xóa suggestion, post gốc không đổi. |

## Notification Types

Backend bắn các type riêng cho improve post:

```ts
const AiPostImproveNotificationTypes = {
  Submitted: 'ai.post_improve.submitted',
  Processing: 'ai.post_improve.processing',
  Completed: 'ai.post_improve.completed',
  Failed: 'ai.post_improve.failed'
} as const;
```

FE nên thêm các type này vào `NotificationTypes`.

Các type nên hide khỏi notification bell:

- `ai.post_improve.submitted`
- `ai.post_improve.processing`

`completed` và `failed` có thể hiện toast/bell tùy UX.

## Payload Realtime

Mỗi notification improve có `payloadJson` cùng shape:

```ts
export type AiPostImproveRealtimePayload = {
  correlationId: string;
  recommendPostId: string;
  originalPostId: string;
  postId: string;
  userId: string;
  workspaceId: string | null;
  status: 'Submitted' | 'Processing' | 'Completed' | 'Failed' | string;
  taskStatus: 'Submitted' | 'Processing' | 'Completed' | 'Failed' | string;
  improveCaption: boolean;
  improveImage: boolean;
  style: string;
  userInstruction: string | null;
  resultCaption: string | null;
  resultResourceId: string | null;
  resultPresignedUrl: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  createdAt: string;
  completedAt: string | null;
};
```

`postId` và `originalPostId` là cùng post gốc. FE có thể dùng `postId` làm key UI.

## Start API

```ts
await startAiPostImprove(postId, {
  improveCaption: true,
  improveImage: true,
  style: 'branded',
  userInstruction: 'Viết caption ngắn hơn và tạo ảnh sạch hơn.'
});
```

Request:

```json
{
  "improveCaption": true,
  "improveImage": true,
  "style": "branded",
  "userInstruction": "Viết caption ngắn hơn và tạo ảnh sạch hơn."
}
```

Rules:

- Ít nhất một trong `improveCaption` hoặc `improveImage` phải là `true`.
- `style` nhận `creative`, `branded`, `marketing`.
- Nếu không gửi `style`, BE dùng style đang lưu trên post gốc, fallback `branded`.

Response `202 Accepted` vẫn trả `AiPostImproveResponse`. FE dùng response này để set optimistic state `Submitted`, nhưng trạng thái tiếp theo phải đến từ SignalR.

## SignalR Flow

FE đang có hub:

```ts
connection.on('NotificationReceived', handleNotification);
```

Khi nhận notification improve:

1. Parse `notification.payloadJson`.
2. Check type thuộc `ai.post_improve.*`.
3. Update cache/local state theo `payload.postId`.
4. Invalidate `['posts']` để list/card nhận metadata mới.
5. Không bật `refetchInterval`.

Ví dụ handler:

```ts
const POST_IMPROVE_TYPES = new Set([
  'ai.post_improve.submitted',
  'ai.post_improve.processing',
  'ai.post_improve.completed',
  'ai.post_improve.failed'
]);

function isPostImproveNotification(type: string) {
  return POST_IMPROVE_TYPES.has(type);
}

function handleImproveNotification(notification: NotificationDelivery) {
  if (!isPostImproveNotification(notification.type)) return;

  const payload = JSON.parse(notification.payloadJson ?? '{}') as AiPostImproveRealtimePayload;
  if (!payload.postId) return;

  queryClient.setQueryData<AiPostImproveResponse>(
    ['ai-post-improve', payload.postId],
    {
      isSuccess: true,
      isFailure: false,
      error: null,
      value: {
        recommendId: payload.recommendPostId,
        correlationId: payload.correlationId,
        status: payload.status,
        originalPostId: payload.originalPostId,
        userId: payload.userId,
        workspaceId: payload.workspaceId,
        improveCaption: payload.improveCaption,
        improveImage: payload.improveImage,
        style: payload.style,
        userInstruction: payload.userInstruction,
        resultCaption: payload.resultCaption,
        resultResourceId: payload.resultResourceId,
        resultPresignedUrl: payload.resultPresignedUrl,
        errorCode: payload.errorCode,
        errorMessage: payload.errorMessage,
        createdAt: payload.createdAt,
        completedAt: payload.completedAt
      }
    }
  );

  queryClient.invalidateQueries({ queryKey: ['posts'] });
}
```

## Không dùng polling

Không dùng pattern này cho improve:

```ts
useQuery({
  queryKey: ['ai-post-improve', postId],
  queryFn: () => fetchAiPostImprove(postId),
  refetchInterval: 3000
});
```

Thay vào đó:

```ts
useQuery({
  queryKey: ['ai-post-improve', postId],
  queryFn: () => fetchAiPostImprove(postId),
  enabled: Boolean(postId) && shouldRecoverFromPageLoad,
  staleTime: Infinity,
  refetchInterval: false
});
```

`shouldRecoverFromPageLoad` chỉ nên `true` khi:

- User mở trực tiếp màn review từ URL.
- Page reload mất state realtime.
- FE thấy `post.aiImproveStatus` đang có nhưng cache `['ai-post-improve', postId]` chưa có value.

## UI State

Map status:

| Status | UI |
|---|---|
| `Submitted` | Task đã queue, show pending. |
| `Processing` | AI đang chạy, disable approve/reject. |
| `Completed` | Show preview và enable Approve/Reject. |
| `Failed` | Show lỗi, enable Reject hoặc Try again. |

Preview:

```ts
const suggestion = improveQuery.data?.value;
const previewCaption = suggestion?.resultCaption ?? originalPost.content?.content ?? '';
const previewImageUrl =
  suggestion?.resultPresignedUrl ?? originalPost.media?.[0]?.presignedUrl ?? null;
```

Nếu `improveCaption=false`, `resultCaption=null`, FE dùng caption gốc.

Nếu `improveImage=false`, `resultPresignedUrl=null`, FE dùng ảnh gốc.

## Approve

```ts
await approveAiPostImprove(postId);
```

Chỉ enable khi:

```ts
suggestion?.status?.toLowerCase() === 'completed'
```

BE behavior:

- `improveCaption=true`: thay `post.content.content` bằng `resultCaption`.
- `improveImage=true`: thay `post.content.resource_list` bằng resource id ảnh mới.
- Giữ hashtag và post type hiện có.
- Xóa `RecommendPost` sau khi apply.

Sau approve:

```ts
queryClient.invalidateQueries({ queryKey: ['posts'] });
queryClient.removeQueries({ queryKey: ['ai-post-improve', postId] });
```

## Reject

```ts
await rejectAiPostImprove(postId);
```

Chỉ enable khi terminal:

```ts
const status = suggestion?.status?.toLowerCase();
const canReject = status === 'completed' || status === 'failed';
```

BE behavior:

- Không sửa post gốc.
- Xóa `RecommendPost`.

Sau reject:

```ts
queryClient.invalidateQueries({ queryKey: ['posts'] });
queryClient.removeQueries({ queryKey: ['ai-post-improve', postId] });
```

## Post List Metadata

`PostResponse` có metadata để render card/list:

```ts
post.aiImproveRecommendPostId
post.aiImproveCorrelationId
post.aiImproveStatus
post.isAiImproving
post.isAiImproveDone
post.aiImproveCompletedAt
post.aiImproveErrorCode
post.aiImproveErrorMessage
```

Mapping:

```ts
const status = post.aiImproveStatus?.toLowerCase();
const isRunning = status === 'submitted' || status === 'processing';
const isReady = status === 'completed';
const isFailed = status === 'failed';
```

## Replace-on-rerun

Mỗi post chỉ có một active suggestion.

Nếu user bấm Try again, BE xóa suggestion cũ và tạo suggestion mới. FE nên confirm:

```text
Tạo lại sẽ thay thế kết quả AI improve hiện tại. Bạn có muốn tiếp tục?
```

Không cho submit lại khi status đang `Submitted` hoặc `Processing`.

## Error

| Code | FE xử lý |
|---|---|
| `ImprovePost.NothingToImprove` | Form error: chọn caption hoặc image. |
| `ImprovePost.InvalidStyle` | Reset style về `branded`. |
| `Post.NotFound` | Post không tồn tại, quay lại list. |
| `Post.Unauthorized` | Không có quyền với post. |
| `RecommendPost.NotFound` | Chưa có suggestion hoặc đã approve/reject. |
| `ImprovePost.NotCompleted` | Disable approve trước khi completed. |
| `ImprovePost.NotFinished` | Disable reject khi task còn chạy. |
| `ImprovePost.MissingResultCaption` | Không apply được caption, cho chạy lại. |
| `ImprovePost.MissingResultResource` | Không apply được image, cho chạy lại. |

Với notification `ai.post_improve.failed`, FE hiển thị `payload.errorMessage`, fallback `payload.errorCode`.

## Checklist FE

- Thêm notification types `ai.post_improve.*`.
- Update `useNotificationHub` để parse payload và set cache realtime.
- Không dùng `refetchInterval` cho `fetchAiPostImprove`.
- Dùng `GET /improve` chỉ để recovery/manual refresh.
- Disable approve khi chưa `Completed`.
- Disable reject khi chưa terminal.
- Sau approve/reject, invalidate `['posts']` và clear `['ai-post-improve', postId]`.
