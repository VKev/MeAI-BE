# Feed Microservice API & Frontend Integration Guide

## Phạm vi tài liệu

Tài liệu này mô tả:

- các API hiện có trong `Feed.Microservice`
- luồng nghiệp vụ chính của từng API
- cách frontend **Vite + React + TypeScript** nên tích hợp
- cách upload media trước khi tạo post
- cách triển khai **infinite scroll** cho feed và comment bằng **tuple pagination**

---

## Base URL và cách truy cập

- Qua API Gateway: `/api/Feed/...`
- Truy cập trực tiếp local service: `http://localhost:5007/api/Feed/...`
- OpenAPI JSON của Feed service: `/openapi/v1.json`
- Scalar docs của Feed service: `/docs`

Gateway route hiện được expose qua prefix `/api/Feed`, nên frontend nên gọi qua gateway để đồng nhất auth/cookie/policy.

---

## Auth và response contract

- Tất cả endpoint trong `FeedController` yêu cầu đăng nhập.
- User hiện tại được backend lấy từ `ClaimTypes.NameIdentifier`.
- Nếu không resolve được user id hợp lệ, backend trả `401` với `MessageResponse("Unauthorized")`.
- Thành công sẽ trả envelope `Result` / `Result<T>`.
- Khi thất bại do business/validation, backend trả `ProblemDetails`.

### Shape nên xử lý ở frontend

#### Success envelope

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": {}
}
```

hoặc:

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": []
}
```

#### Error envelope phổ biến

```json
{
  "type": "...",
  "title": "...",
  "status": 400,
  "detail": "...",
  "errors": {
    "field": ["message"]
  }
}
```

Frontend không nên giả định backend trả `{ data, success, error }` kiểu custom.

---

## Danh sách API hiện tại

| Method | Route | Mục đích |
|---|---|---|
| POST | `/api/Feed/posts` | Tạo bài viết mới |
| GET | `/api/Feed/posts/feed` | Lấy feed của user hiện tại, có cursor pagination |
| GET | `/api/Feed/posts/{id}` | Lấy chi tiết một bài viết |
| POST | `/api/Feed/posts/{id}/like` | Like một bài viết |
| DELETE | `/api/Feed/posts/{id}/like` | Unlike một bài viết |
| DELETE | `/api/Feed/posts/{id}` | Xóa mềm bài viết của chính mình |
| POST | `/api/Feed/comments` | Tạo comment gốc cho bài viết |
| GET | `/api/Feed/posts/{id}/comments` | Lấy **root comments** của bài viết, có cursor pagination |
| GET | `/api/Feed/comments/{id}/replies` | Lấy replies của một comment, có cursor pagination |
| POST | `/api/Feed/comments/{id}/reply` | Tạo reply cho một comment |
| POST | `/api/Feed/follow/{userId}` | Follow một user |
| DELETE | `/api/Feed/follow/{userId}` | Unfollow một user |
| GET | `/api/Feed/followers/{userId}` | Lấy danh sách followers |
| GET | `/api/Feed/following/{userId}` | Lấy danh sách following |
| POST | `/api/Feed/reports` | Tạo report cho post hoặc comment |
| GET | `/api/Feed/admin/reports` | Lấy toàn bộ report cho admin |

---

## Các model response chính

### `PostResponse`

- `id`
- `userId`
- `content`
- `mediaUrl`
- `mediaType`
- `media`: danh sách media đã resolve presigned URL
- `likesCount`
- `isLikedByCurrentUser`
- `commentsCount`
- `sharesCount`
- `hashtags`
- `createdAt`
- `updatedAt`

### `PostLikeResponse`

- `postId`
- `likesCount`
- `isLikedByCurrentUser`

### `CommentResponse`

- `id`
- `postId`
- `userId`
- `parentCommentId`
- `content`
- `likesCount`
- `repliesCount`
- `createdAt`
- `updatedAt`

### `FollowUserResponse`

- `userId`
- `followedAt`

### `ReportResponse`

- `id`
- `reporterId`
- `targetType`
- `targetId`
- `reason`
- `status`
- `createdAt`
- `updatedAt`

---

## TypeScript contracts gợi ý cho frontend

Nếu frontend dùng Vite + React + TypeScript, nên tạo types gần với response thật của backend.

```ts
export interface Result<T> {
  isSuccess: boolean;
  isFailure: boolean;
  value: T;
  error?: {
    code: string;
    message: string;
  };
}

export interface ProblemDetailsResponse {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

export interface MessageResponse {
  message: string;
}

export interface PostMediaResponse {
  resourceId: string;
  presignedUrl: string;
  contentType: string;
  resourceType: string;
}

export interface PostResponse {
  id: string;
  userId: string;
  content: string | null;
  mediaUrl: string | null;
  mediaType: string | null;
  media: PostMediaResponse[];
  likesCount: number;
  isLikedByCurrentUser: boolean;
  commentsCount: number;
  sharesCount: number;
  hashtags: string[];
  createdAt: string | null;
  updatedAt: string | null;
}

export interface PostLikeResponse {
  postId: string;
  likesCount: number;
  isLikedByCurrentUser: boolean;
}

export interface CommentResponse {
  id: string;
  postId: string;
  userId: string;
  parentCommentId: string | null;
  content: string;
  likesCount: number;
  repliesCount: number;
  createdAt: string | null;
  updatedAt: string | null;
}

export interface FeedCursor {
  cursorCreatedAt?: string;
  cursorId?: string;
  limit?: number;
}
```

---

## Hướng tổ chức frontend với Vite + React + TypeScript

## 1. Nên tách theo modules

Gợi ý cấu trúc:

```text
src/
  features/
    feed/
      api/
        feedApi.ts
        resourceApi.ts
      hooks/
        useFeedInfiniteQuery.ts
        useCreatePost.ts
        usePostComments.ts
        useCommentReplies.ts
      types/
        feed.types.ts
      components/
        FeedList.tsx
        FeedComposer.tsx
        PostCard.tsx
        CommentList.tsx
        ReplyList.tsx
```

## 2. Nên dùng React Query

Với Feed API hiện tại, React Query rất hợp vì:

- có infinite query cho cursor pagination
- cache theo query key dễ
- dễ invalidate sau khi create post / like / unlike / comment / follow / unfollow
- phù hợp cho retry/loading/error state

## 3. Nên có một API client chung

Nếu hệ thống auth qua cookie thì nhớ bật `credentials: 'include'`.

```ts
export async function apiFetch<T>(input: RequestInfo, init?: RequestInit): Promise<T> {
  const response = await fetch(input, {
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
    ...init,
  });

  if (!response.ok) {
    const errorBody = await response.json().catch(() => null);
    throw errorBody ?? new Error('Request failed');
  }

  return response.json() as Promise<T>;
}
```

Với upload file thì **không set `Content-Type: application/json`**; dùng `FormData` để browser tự set multipart boundary.

---

## Luồng tạo post có media ở frontend

## Kết luận ngắn

Nếu post có ảnh/video thì frontend phải:

1. upload file lên User service trước
2. lấy `resourceId`
3. gọi API tạo post của Feed với `resourceIds`

## Bước 1: upload resource

### Endpoint

`POST /api/User/resources`

### Content type

`multipart/form-data`

### Form fields

- `file`: file bắt buộc
- `status`: optional
- `resourceType`: optional

### Gợi ý frontend

```ts
export async function uploadResource(file: File, resourceType?: string) {
  const formData = new FormData();
  formData.append('file', file);

  if (resourceType) {
    formData.append('resourceType', resourceType);
  }

  return fetch('/api/User/resources', {
    method: 'POST',
    credentials: 'include',
    body: formData,
  }).then(async (res) => {
    const body = await res.json();
    if (!res.ok) throw body;
    return body;
  });
}
```

## Bước 2: tạo post

### Endpoint

`POST /api/Feed/posts`

### Body

```json
{
  "content": "hello world #react",
  "resourceIds": ["resource-guid-1"],
  "mediaType": "Image"
}
```

### Gợi ý thực tế

Frontend composer nên chạy theo flow:

1. user chọn file
2. upload từng file hoặc upload song song
3. gom `resourceId[]`
4. submit create post
5. invalidate query feed đầu trang

## Gợi ý UX

- disable nút submit trong lúc upload
- hiển thị progress upload riêng với mỗi file
- nếu upload fail thì không gọi create post
- cho phép create post text-only khi `resourceIds` rỗng và `content` có giá trị

---

## 1) POST `/api/Feed/posts`

### Request body

```json
{
  "content": "string | null",
  "resourceIds": ["guid"],
  "mediaType": "string | null"
}
```

### Validation

- `content` tối đa 5000 ký tự nếu có truyền
- `resourceIds` nếu có thì mọi phần tử phải là GUID hợp lệ
- post phải có `content` hoặc ít nhất 1 `resourceId`

### Luồng backend

1. chuẩn hóa `content`, `resourceIds`, `mediaType`
2. nếu có `resourceIds`, Feed gọi User service để resolve resource
3. nếu số lượng resource resolve không khớp, trả lỗi
4. trích hashtag từ `content`
5. tạo `Post`
6. upsert hashtag và `PostHashtag`
7. lấy followers của user tạo post
8. gọi AI service để mirror post
9. publish notification
10. trả `PostResponse`

### Gợi ý frontend

- optimistic UI cho create post có thể làm, nhưng chỉ nên append vào đầu feed sau khi API create post thành công
- vì `PostResponse` đã có media presigned URL, frontend có thể render ngay mà không cần gọi thêm API media
- sau create success, nên reset input composer + preview files + invalidate feed query đầu tiên

---

## 2) GET `/api/Feed/posts/feed`

### Query params

- `cursorCreatedAt`: optional, nhưng nếu có thì phải đi cùng `cursorId`
- `cursorId`: optional, nhưng nếu có thì phải đi cùng `cursorCreatedAt`
- `limit`: optional, backend tự clamp từ 1 đến 100, mặc định 50

### Ví dụ request đầu tiên

```http
GET /api/Feed/posts/feed?limit=20
```

### Ví dụ request trang tiếp theo

```http
GET /api/Feed/posts/feed?cursorCreatedAt=2026-04-18T07:00:00Z&cursorId=3fa85f64-5717-4562-b3fc-2c963f66afa6&limit=20
```

### Luồng backend

1. lấy danh sách user đang follow
2. thêm chính current user vào nguồn feed
3. lấy post chưa bị xóa của các user đó
4. nếu có cursor thì filter bằng tuple `(CreatedAt, Id)`
5. sort `CreatedAt desc`, `Id desc`
6. lấy `limit` phần tử
7. resolve media + hashtag
8. trả `IReadOnlyList<PostResponse>`

### Gợi ý frontend với React Query

Nên dùng `useInfiniteQuery` và tự tạo `nextCursor` từ item cuối cùng của page hiện tại.

```ts
function getNextCursor(posts: PostResponse[]) {
  const last = posts[posts.length - 1];
  if (!last?.createdAt || !last?.id) return undefined;

  return {
    cursorCreatedAt: last.createdAt,
    cursorId: last.id,
  };
}
```

```ts
// pseudo-code
useInfiniteQuery({
  queryKey: ['feed'],
  initialPageParam: { limit: 20 },
  queryFn: ({ pageParam }) => getFeed(pageParam),
  getNextPageParam: (lastPage) => {
    const items = lastPage.value;
    if (!items.length) return undefined;
    return {
      ...getNextCursor(items),
      limit: 20,
    };
  },
});
```

### Gợi ý UI

- feed screen nên dùng infinite scroll hoặc nút “load more”
- nếu số item trả về `< limit` thì coi như gần hết dữ liệu
- nên dedupe theo `post.id` khi merge pages nếu có refetch đồng thời

---

## 3) GET `/api/Feed/posts/{id}`

### Mục đích

Lấy chi tiết một post để hiển thị ở post detail page / modal detail.

### Gợi ý frontend

- dùng khi user mở modal hoặc route `/posts/:id`
- nếu đã có post trong feed cache thì có thể hydrate UI trước, sau đó fetch detail để đồng bộ
- nếu API trả `Feed.Post.NotFound`, frontend nên hiển thị trạng thái “Bài viết không tồn tại hoặc đã bị xóa”
- response hiện đã có `isLikedByCurrentUser`, nên frontend không cần gọi thêm API riêng để biết trạng thái like hiện tại

---

## 4) POST `/api/Feed/posts/{id}/like`

### Mục đích

Like một bài viết. Một user chỉ được like một lần trên cùng một post.

### Success response

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": {
    "postId": "guid",
    "likesCount": 12,
    "isLikedByCurrentUser": true
  }
}
```

### Luồng backend

1. kiểm tra post còn tồn tại và chưa bị xóa mềm
2. kiểm tra current user đã like post hay chưa
3. nếu chưa like, tạo `PostLike`
4. tăng `posts.likes_count`
5. cập nhật `posts.updated_at`
6. commit transaction
7. trả `PostLikeResponse`

### Gợi ý frontend

- có thể optimistic update `likesCount + 1` và `isLikedByCurrentUser = true`
- nếu backend trả `Feed.Post.Like.Exists`, frontend nên rollback optimistic state hoặc refetch post detail/feed item tương ứng
- nên cập nhật đồng thời ở cả feed cache lẫn post detail cache nếu cùng đang mở

---

## 5) DELETE `/api/Feed/posts/{id}/like`

### Mục đích

Unlike một bài viết mà current user đã like trước đó.

### Success response

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": {
    "postId": "guid",
    "likesCount": 11,
    "isLikedByCurrentUser": false
  }
}
```

### Luồng backend

1. kiểm tra post còn tồn tại và chưa bị xóa mềm
2. tìm bản ghi `PostLike` của current user trên post đó
3. nếu tồn tại, xóa `PostLike`
4. giảm `posts.likes_count` nhưng không nhỏ hơn `0`
5. cập nhật `posts.updated_at`
6. commit transaction
7. trả `PostLikeResponse`

### Gợi ý frontend

- có thể optimistic update `likesCount - 1` và `isLikedByCurrentUser = false`
- nếu backend trả `Feed.Post.Like.NotFound`, frontend nên đồng bộ lại state từ server thay vì tiếp tục decrement local count
- mutation unlike nên dùng cùng query-key invalidation strategy với like

---

## 6) DELETE `/api/Feed/posts/{id}`

### Gợi ý frontend

- chỉ hiển thị nút xóa khi `post.userId === currentUser.id`
- sau delete success:
  - remove post khỏi cache feed
  - đóng modal detail nếu đang ở màn chi tiết
- nếu backend trả `Feed.Forbidden`, cần fallback bằng toast lỗi và refetch post list

---

## 7) POST `/api/Feed/comments`

### Request body

```json
{
  "postId": "guid",
  "content": "string"
}
```

### Gợi ý frontend

- đây là API tạo **root comment**
- sau create success có 2 hướng:
  - prepend comment mới vào danh sách root comments nếu đang mở comment panel
  - hoặc invalidate query `['post-comments', postId]`
- đồng thời có thể tăng `commentsCount` ở post card trong local cache

---

## 8) GET `/api/Feed/posts/{id}/comments`

### Mục đích hiện tại

API này **chỉ lấy root comments** của bài post, tức chỉ comment có `ParentCommentId = null`.

### Query params

- `cursorCreatedAt`
- `cursorId`
- `limit`

### Ví dụ request

```http
GET /api/Feed/posts/{postId}/comments?limit=10
```

### Luồng backend

1. check post tồn tại
2. chỉ query comment depth 1
3. nếu có cursor thì áp dụng tuple pagination
4. sort `CreatedAt desc`, `Id desc`
5. lấy `limit`
6. trả `CommentResponse[]`

### Gợi ý frontend

- comment panel chỉ render root comments ở level đầu
- mỗi root comment có thể có nút `View replies (repliesCount)`
- không nên cố build full comment tree chỉ từ API này
- dùng `useInfiniteQuery` giống feed posts

### Shape state gợi ý

```ts
interface CommentThreadState {
  rootComments: CommentResponse[];
  repliesByParentId: Record<string, CommentResponse[]>;
  expandedParentIds: Record<string, boolean>;
}
```

---

## 9) GET `/api/Feed/comments/{id}/replies`

### Mục đích

Lấy replies của một comment cụ thể, cũng có pagination.

### Query params

- `cursorCreatedAt`
- `cursorId`
- `limit`

### Ví dụ request

```http
GET /api/Feed/comments/{commentId}/replies?limit=10
```

### Gợi ý frontend

- chỉ fetch khi user bấm “xem trả lời” hoặc expand thread
- cache riêng theo `commentId`
- nếu đóng thread thì có thể giữ cache để mở lại nhanh
- nếu reply count lớn, tiếp tục dùng “load more replies” hoặc infinite list con

### Flow UI hợp lý

1. load root comments trước
2. user expand 1 root comment
3. fetch replies cho root comment đó
4. user có thể load thêm replies theo cursor
5. user reply mới => invalidate replies query của comment cha

---

## 10) POST `/api/Feed/comments/{id}/reply`

### Request body

```json
{
  "content": "string"
}
```

### Gợi ý frontend

- nút reply nên gắn với từng comment card
- sau reply thành công:
  - tăng `repliesCount` của comment cha trong cache
  - nếu thread đang mở, có thể prepend reply mới vào replies list
  - nếu thread chưa mở, chỉ cần tăng count và chờ user expand sau

---

## 11) POST `/api/Feed/follow/{userId}` và DELETE `/api/Feed/follow/{userId}`

### Gợi ý frontend

- nên dùng mutation riêng cho follow/unfollow
- sau follow/unfollow success:
  - cập nhật local profile header
  - invalidate followers/following queries nếu có
  - có thể invalidate feed nếu business muốn thấy post mới của user vừa follow

---

## 12) GET `/api/Feed/followers/{userId}`

### Gợi ý frontend

- thích hợp cho modal followers list
- hiện chưa có pagination nên nên cẩn thận nếu user lớn
- nếu cần render avatar/name thì frontend có thể phải join với user profile source khác, vì API này chỉ trả `userId` và `followedAt`

---

## 13) GET `/api/Feed/following/{userId}`

### Gợi ý frontend

- tương tự followers
- hiện phù hợp cho modal/tab profile “Following”
- cũng cần resolve user profile ở tầng frontend hoặc từ service khác

---

## 14) POST `/api/Feed/reports`

### Request body

```json
{
  "targetType": "Post | Comment",
  "targetId": "guid",
  "reason": "string"
}
```

### Gợi ý frontend

- dùng modal report chung cho cả post và comment
- map `targetType` đúng casing backend đang chấp nhận: `Post` hoặc `Comment`
- sau submit thành công chỉ cần toast “Đã gửi báo cáo”
- không cần invalidate feed ngay

---

## 15) GET `/api/Feed/admin/reports`

### Gợi ý frontend

- chỉ dùng ở admin area
- frontend nên guard route theo role trước khi render màn admin
- nếu backend trả `401/403` thì chuyển hướng khỏi admin page

---

## Hướng triển khai comment tree ở frontend

Vì backend hiện chia comment thành 2 lớp API:

- root comments: `/posts/{id}/comments`
- replies theo parent: `/comments/{id}/replies`

frontend nên dùng chiến lược **lazy thread loading**:

### Cách làm khuyến nghị

- chỉ render root comments khi mở post detail
- mỗi root comment hiển thị `repliesCount`
- user bấm “xem trả lời” mới gọi API replies
- mỗi thread con có pagination riêng
- không fetch toàn bộ cây comment một lần

### Lợi ích

- giảm payload ban đầu
- giảm số lượng DOM node khi post có nhiều thread
- phù hợp với backend hiện tại
- dễ tối ưu performance và cache hơn trong React

---

## Hướng triển khai feed infinite scroll ở frontend

### Cursor rule

Frontend phải lấy cursor từ **item cuối cùng của page hiện tại**:

- `cursorCreatedAt = lastItem.createdAt`
- `cursorId = lastItem.id`

### Lưu ý quan trọng

- backend yêu cầu `cursorCreatedAt` và `cursorId` phải đi cùng nhau
- không gửi 1 trong 2 giá trị riêng lẻ
- nên giữ `limit` cố định giữa các lần fetch cùng một list

### Khuyến nghị

- feed posts: `limit = 10` hoặc `20`
- root comments: `limit = 10`
- replies: `limit = 10`

---

## Mapping query keys gợi ý cho React Query

```ts
export const feedKeys = {
  all: ['feed'] as const,
  list: (limit: number) => ['feed', 'list', { limit }] as const,
  detail: (postId: string) => ['feed', 'detail', postId] as const,
  postComments: (postId: string) => ['feed', 'comments', postId] as const,
  commentReplies: (commentId: string) => ['feed', 'replies', commentId] as const,
  followers: (userId: string) => ['feed', 'followers', userId] as const,
  following: (userId: string) => ['feed', 'following', userId] as const,
};
```

---

## Tích hợp UI đề xuất cho từng màn

## Home feed

- composer tạo post
- list post infinite scroll
- mỗi post card có preview media, hashtags, comments count
- click comments mở drawer/modal post detail

## Post detail modal/page

- fetch post detail nếu cần
- load root comments theo trang đầu
- expand replies theo từng comment
- cho phép create root comment và reply

## User profile

- nút follow/unfollow
- tab followers/following
- nếu cần danh sách post của user thì hiện Feed service chưa có endpoint chuyên biệt cho profile posts

## Admin reports page

- bảng report list
- filter/search hiện chưa có từ backend, frontend chỉ nên làm client-side tạm thời nếu data nhỏ

---

## Các lỗi nghiệp vụ frontend nên map rõ

- `Feed.Post.Empty`: hiển thị “Bài viết cần có nội dung hoặc ít nhất một media”
- `Feed.Resource.Missing`: hiển thị “Một hoặc nhiều media upload chưa hợp lệ”
- `Feed.Post.NotFound`: hiển thị “Bài viết không tồn tại hoặc đã bị xóa”
- `Feed.Post.Like.Exists`: hiển thị “Bạn đã like bài viết này rồi”
- `Feed.Post.Like.NotFound`: hiển thị “Bạn chưa like bài viết này”
- `Feed.Comment.NotFound`: hiển thị “Bình luận không tồn tại”
- `Feed.Follow.Self`: hiển thị “Bạn không thể tự follow chính mình”
- `Feed.Follow.Exists`: hiển thị “Bạn đã follow người dùng này rồi”
- `Feed.Follow.NotFound`: hiển thị “Quan hệ follow không tồn tại”
- `Feed.Forbidden`: hiển thị “Bạn không có quyền thực hiện thao tác này”
- `Feed.Report.Target`: hiển thị “Loại đối tượng report không hợp lệ”
- `Feed.Report.Reason`: hiển thị “Vui lòng nhập lý do báo cáo”

---

## Tổng kết hành vi hệ thống

### 1. Validation pipeline

Các query pagination mới có rule chung:

- nếu gửi cursor thì phải gửi đủ `cursorCreatedAt` và `cursorId`
- `limit` sẽ được backend clamp về khoảng hợp lệ

Ngoài ra command chính vẫn đi qua FluentValidation trước khi handler chạy.

### 2. Cơ chế commit

- query chỉ đọc dữ liệu
- command chỉ commit khi handler trả success
- nếu handler trả `Result` failure thì không commit

### 3. Tích hợp service khác

- Feed gọi **User service** để resolve media presigned URL
- Feed gọi **AI service** khi tạo post để mirror post
- Feed publish event qua **MassTransit/RabbitMQ** cho notification

### 4. Giới hạn hiện tại

- followers/following hiện chưa có pagination
- chưa có comment like/share/search/recommendation API
- API feed hiện là **follow-based feed**
- comment tree được load theo từng tầng, không có API trả full nested tree trong một response
