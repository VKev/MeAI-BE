# Feed Microservice API & Frontend Integration Guide

## Phạm vi tài liệu

Tài liệu này phản ánh trạng thái hiện tại của `Feed.Microservice`, bao gồm:

- các route đang được expose bởi `FeedController`
- auth và response contract hiện tại
- request/response models quan trọng cho frontend
- quy tắc pagination bằng cursor
- các lưu ý tích hợp cho feed, profile, comments, follow và moderation

---

## Base URL

Frontend nên gọi qua API Gateway với prefix:

- `/api/Feed/...`

Truy cập trực tiếp service local nếu cần:

- `http://localhost:5007/api/Feed/...`

Tài nguyên OpenAPI/Docs của service:

- OpenAPI JSON: `/openapi/v1.json`
- Docs UI: `/docs`

---

## Auth và response contract

`FeedController` mặc định yêu cầu đăng nhập. Một số endpoint đọc dữ liệu cho phép anonymous.

### Public/anonymous endpoints

- `GET /api/Feed/profiles/{username}`
- `GET /api/Feed/profiles/{username}/posts`
- `GET /api/Feed/posts/{id}`
- `GET /api/Feed/posts/{id}/comments`
- `GET /api/Feed/comments/{id}/replies`

### Auth-required endpoints

Tất cả route còn lại yêu cầu user hợp lệ từ `ClaimTypes.NameIdentifier`.

Nếu không resolve được `userId`, backend trả:

```json
{
  "message": "Unauthorized"
}
```

với HTTP `401`.

### Success contract

Các endpoint success trả envelope `Result` hoặc `Result<T>`.

Ví dụ object:

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": {}
}
```

Ví dụ list:

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": []
}
```

### Error contract

Các lỗi business/validation đi qua `ProblemDetails`.

Ví dụ phổ biến:

```json
{
  "type": "Feed.Post.NotFound",
  "title": "Bad Request",
  "status": 400,
  "detail": "The requested post was not found.",
  "errors": {
    "field": ["message"]
  }
}
```

Frontend không nên giả định backend trả custom envelope kiểu `{ success, data, error }`.

---

## Danh sách API hiện tại

| Method | Route | Auth | Mục đích |
|---|---|---|---|
| GET | `/api/Feed/profiles/{username}` | Anonymous | Lấy public profile theo username |
| GET | `/api/Feed/profiles/{username}/posts` | Anonymous | Lấy danh sách post theo username với cursor pagination |
| POST | `/api/Feed/posts` | Required | Tạo post mới |
| GET | `/api/Feed/posts/feed` | Required | Lấy home feed của user hiện tại |
| GET | `/api/Feed/posts/{id}` | Anonymous | Lấy chi tiết post |
| POST | `/api/Feed/posts/{id}/like` | Required | Like post |
| DELETE | `/api/Feed/posts/{id}/like` | Required | Unlike post |
| PUT | `/api/Feed/posts/{id}` | Required | Cập nhật post của chính mình |
| DELETE | `/api/Feed/posts/{id}` | Required | Xóa mềm post của chính mình |
| POST | `/api/Feed/comments` | Required | Tạo root comment cho post |
| GET | `/api/Feed/posts/{id}/comments` | Anonymous | Lấy root comments của post với cursor pagination |
| GET | `/api/Feed/comments/{id}/replies` | Anonymous | Lấy replies của một comment với cursor pagination |
| POST | `/api/Feed/comments/{id}/like` | Required | Like comment |
| DELETE | `/api/Feed/comments/{id}/like` | Required | Unlike comment |
| POST | `/api/Feed/comments/{id}/reply` | Required | Tạo reply cho comment |
| DELETE | `/api/Feed/comments/{id}` | Required | Xóa comment/thread của post owner |
| POST | `/api/Feed/follow/{userId}` | Required | Follow một user |
| DELETE | `/api/Feed/follow/{userId}` | Required | Unfollow một user |
| GET | `/api/Feed/followers/{userId}` | Required | Lấy danh sách followers |
| GET | `/api/Feed/following/{userId}` | Required | Lấy danh sách following |
| GET | `/api/Feed/follow/suggestions` | Required | Gợi ý user nên follow |
| POST | `/api/Feed/reports` | Required | Tạo report cho post hoặc comment |
| GET | `/api/Feed/admin/reports` | Admin | Lấy danh sách report cho admin |
| PATCH | `/api/Feed/admin/reports/{id}` | Admin | Review report và áp dụng moderation action |

---

## Response models chính

### `PublicProfileResponse`

```ts
interface PublicProfileResponse {
  userId: string;
  username: string;
  fullName: string | null;
  avatarUrl: string | null;
  followersCount: number;
  followingCount: number;
}
```

### `PostMediaResponse`

```ts
interface PostMediaResponse {
  resourceId: string;
  presignedUrl: string;
  contentType: string;
  resourceType: string;
}
```

### `PostResponse`

```ts
interface PostResponse {
  id: string;
  userId: string;
  username: string;
  avatarUrl: string | null;
  content: string | null;
  mediaUrl: string | null;
  mediaType: string | null;
  media: PostMediaResponse[];
  likesCount: number;
  commentsCount: number;
  hashtags: string[];
  createdAt: string | null;
  updatedAt: string | null;
  isLikedByCurrentUser: boolean | null;
  canDelete: boolean | null;
}
```

Lưu ý:

- `isLikedByCurrentUser` có thể là `null` khi request anonymous.
- `canDelete` có thể là `null` khi request anonymous.
- Phase hiện tại chưa có `canEdit` trong contract.

### `PostLikeResponse`

```ts
interface PostLikeResponse {
  postId: string;
  likesCount: number;
  isLikedByCurrentUser: boolean;
}
```

### `CommentLikeResponse`

```ts
interface CommentLikeResponse {
  commentId: string;
  likesCount: number;
  isLikedByCurrentUser: boolean;
}
```

### `CommentResponse`

```ts
interface CommentResponse {
  id: string;
  postId: string;
  userId: string;
  username: string;
  avatarUrl: string | null;
  parentCommentId: string | null;
  content: string;
  likesCount: number;
  repliesCount: number;
  createdAt: string | null;
  updatedAt: string | null;
  isLikedByCurrentUser: boolean | null;
  canDelete: boolean | null;
}
```

Lưu ý:

- `username` và `avatarUrl` đã được hydrate sẵn từ User service.
- Backend resolve profile theo batch distinct `userId` trong từng page comments/replies để tránh N+1 calls.
- `isLikedByCurrentUser` có thể là `null` khi request anonymous.

### `FollowUserResponse`

```ts
interface FollowUserResponse {
  userId: string;
  followedAt: string | null;
}
```

### `FollowSuggestionResponse`

```ts
interface FollowSuggestionResponse {
  userId: string;
  username: string;
  fullName: string | null;
  avatarUrl: string | null;
  postCount: number;
}
```

### `ReportResponse`

```ts
interface ReportResponse {
  id: string;
  reporterId: string;
  targetType: string;
  targetId: string;
  reason: string;
  status: string;
  reviewedByAdminId: string | null;
  reviewedAt: string | null;
  resolutionNote: string | null;
  actionType: string | null;
  createdAt: string | null;
  updatedAt: string | null;
}
```

---

## Shared TypeScript contracts gợi ý

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

export interface FeedCursor {
  cursorCreatedAt?: string;
  cursorId?: string;
  limit?: number;
}
```

---

## Pagination rules

Các list dùng cursor pagination trong Feed hiện tại:

- `GET /api/Feed/profiles/{username}/posts`
- `GET /api/Feed/posts/feed`
- `GET /api/Feed/posts/{id}/comments`
- `GET /api/Feed/comments/{id}/replies`

### Query params

- `cursorCreatedAt`: optional
- `cursorId`: optional
- `limit`: optional

### Quy tắc bắt buộc

- nếu gửi cursor thì phải gửi đủ cả `cursorCreatedAt` và `cursorId`
- backend clamp `limit` trong khoảng `1..100`
- nếu không truyền `limit`, backend dùng mặc định `50`

### Cursor strategy cho frontend

Frontend cần lấy cursor từ item cuối cùng của page hiện tại:

```ts
function getNextCursor<T extends { id: string; createdAt: string | null }>(items: T[]) {
  const last = items[items.length - 1];
  if (!last?.createdAt || !last?.id) return undefined;

  return {
    cursorCreatedAt: last.createdAt,
    cursorId: last.id,
  };
}
```

---

## Frontend integration architecture gợi ý

```text
src/
  features/
    feed/
      api/
        feedApi.ts
        resourceApi.ts
      hooks/
        useFeedInfiniteQuery.ts
        useProfilePostsInfiniteQuery.ts
        usePostCommentsInfiniteQuery.ts
        useCommentRepliesInfiniteQuery.ts
        useCreatePost.ts
        useUpdatePost.ts
        useDeletePost.ts
        useLikePost.ts
        useUnlikePost.ts
      types/
        feed.types.ts
      components/
        FeedComposer.tsx
        PostCard.tsx
        CommentList.tsx
        ReplyList.tsx
        FollowSuggestions.tsx
```

Khuyến nghị dùng React Query để:

- quản lý infinite queries
- cache theo route/query params
- invalidate đồng bộ sau mutation
- hỗ trợ retry/loading/error state

---

## API client gợi ý

Nếu auth đi qua cookie, nhớ bật `credentials: 'include'`.

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

Với upload file thì không set `Content-Type: application/json`; hãy dùng `FormData`.

---

## Upload media trước khi tạo hoặc sửa post

Feed không upload file trực tiếp. Nếu post có media, frontend cần:

1. upload file sang User service trước
2. lấy `resourceId`
3. gọi Feed API với `resourceIds`

### Upload resource

- Endpoint: `POST /api/User/resources`
- Content-Type: `multipart/form-data`

Ví dụ:

```ts
export async function uploadResource(file: File, resourceType?: string) {
  const formData = new FormData();
  formData.append('file', file);

  if (resourceType) {
    formData.append('resourceType', resourceType);
  }

  const response = await fetch('/api/User/resources', {
    method: 'POST',
    credentials: 'include',
    body: formData,
  });

  const body = await response.json().catch(() => null);
  if (!response.ok) throw body;
  return body;
}
```

### Gợi ý UX

- disable submit trong lúc upload
- hiển thị progress theo từng file nếu có
- nếu upload fail thì không gọi Feed create/update post
- cho phép text-only post khi không có media nhưng có nội dung

---

## Chi tiết từng API

## 1) GET `/api/Feed/profiles/{username}`

### Mục đích

Lấy public profile theo username để render header profile công khai.

### Validation

- `username` bắt buộc
- `username` tối đa `100` ký tự

### Gợi ý frontend

- dùng được cho guest page
- nếu lỗi `Feed.User.NotFound`, render trạng thái profile không tồn tại
- counts follower/following đã được backend tổng hợp sẵn

---

## 2) GET `/api/Feed/profiles/{username}/posts`

### Query params

- `cursorCreatedAt`
- `cursorId`
- `limit`

### Mục đích

Lấy timeline post public của một username.

### Validation

- `username` bắt buộc, tối đa `100` ký tự
- `cursorCreatedAt` và `cursorId` phải đi cùng nhau
- `limit` được normalize/clamp bởi backend

### Gợi ý frontend

- phù hợp cho tab posts trong public profile
- khi viewer anonymous, `isLikedByCurrentUser` và `canDelete` sẽ là `null`
- response đã có sẵn `username`, `avatarUrl`, media và hashtags để render post card

---

## 3) POST `/api/Feed/posts`

### Request body

```json
{
  "content": "string | null",
  "resourceIds": ["guid"],
  "mediaType": "string | null"
}
```

### Validation

- `content` tối đa `5000` ký tự nếu có truyền
- `resourceIds` không được chứa `Guid.Empty`
- post phải có `content` hoặc ít nhất một `resourceId`

### Luồng backend tổng quát

1. normalize `content`, `resourceIds`, `mediaType`
2. validate post không rỗng
3. nếu có `resourceIds`, Feed resolve lại ownership/resource từ User service
4. extract hashtag từ `content`
5. tạo post và quan hệ hashtag
6. build `PostResponse`

### Gợi ý frontend

- invalidate feed query đầu trang sau create thành công
- vì response đã có media presigned URL, frontend có thể render ngay
- với create response, frontend nên dùng current profile local nếu cần fallback author state tức thời

---

## 4) GET `/api/Feed/posts/feed`

### Query params

- `cursorCreatedAt`
- `cursorId`
- `limit`

### Mục đích

Lấy home feed follow-based của user hiện tại.

### Luồng backend tổng quát

1. lấy tập user mà current user đang follow
2. thêm chính current user vào nguồn feed
3. lấy post chưa bị xóa
4. áp dụng tuple cursor nếu có
5. sort `CreatedAt desc`, `Id desc`
6. resolve media, hashtag và viewer flags

### Gợi ý frontend

Dùng `useInfiniteQuery` và tự tạo cursor từ item cuối cùng của page.

```ts
useInfiniteQuery({
  queryKey: ['feed', 'list', { limit: 20 }],
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

---

## 5) GET `/api/Feed/posts/{id}`

### Mục đích

Lấy chi tiết một post để hiển thị ở detail page hoặc modal.

### Gợi ý frontend

- có thể hydrate từ feed cache trước khi fetch detail
- nếu lỗi `Feed.Post.NotFound`, hiển thị trạng thái bài viết không còn tồn tại
- endpoint cho phép anonymous

---

## 6) POST `/api/Feed/posts/{id}/like`

### Mục đích

Like một post. Một user chỉ được like một lần trên cùng một post.

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

### Gợi ý frontend

- có thể optimistic update `likesCount + 1`
- nếu backend trả `Feed.Post.Like.Exists`, rollback hoặc refetch item tương ứng
- nên cập nhật cả feed cache và post detail cache nếu cùng đang mở

---

## 7) DELETE `/api/Feed/posts/{id}/like`

### Mục đích

Unlike post mà current user đã like trước đó.

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

### Gợi ý frontend

- có thể optimistic update `likesCount - 1`
- nếu backend trả `Feed.Post.Like.NotFound`, đồng bộ lại state từ server

---

## 8) PUT `/api/Feed/posts/{id}`

### Request body

```json
{
  "content": "string | null",
  "resourceIds": ["guid"],
  "mediaType": "string | null"
}
```

### Mục đích

Cho phép user cập nhật post của chính mình.

### Validation

- `postId` phải hợp lệ
- `content` tối đa `5000` ký tự nếu có truyền
- `resourceIds` không được chứa `Guid.Empty`
- sau khi normalize, post vẫn phải có `content` hoặc ít nhất một `resourceId`

### Hành vi backend hiện tại

- chỉ owner mới được update post
- backend revalidate `resourceIds` qua User service
- backend reconcile lại hashtag theo content mới
- `updatedAt` được cập nhật
- response vẫn là `Result<PostResponse>`
- phase hiện tại chưa sync edit sang AI mirror

### Gợi ý frontend

- chỉ hiển thị nút edit khi `post.userId === currentUser.id`
- sau update success, cập nhật item tương ứng trong feed/detail cache
- nếu user xóa toàn bộ text và media, backend sẽ trả `Feed.Post.Empty`

---

## 9) DELETE `/api/Feed/posts/{id}`

### Mục đích

Xóa mềm post của chính mình.

### Gợi ý frontend

- chỉ hiển thị nút xóa khi `post.userId === currentUser.id`
- sau delete success:
  - remove post khỏi feed cache
  - đóng detail modal/page nếu đang hiển thị post đó
- nếu backend trả `Feed.Forbidden`, nên refetch lại state hiện tại

---

## 10) POST `/api/Feed/comments`

### Request body

```json
{
  "postId": "guid",
  "content": "string"
}
```

### Validation

- `postId` bắt buộc
- `content` bắt buộc
- `content` tối đa `2000` ký tự

### Gợi ý frontend

- đây là API tạo root comment
- response đã có `username`, `avatarUrl`, `isLikedByCurrentUser` để append trực tiếp vào cache UI
- sau success có thể prepend comment mới hoặc invalidate query comments của post
- đồng thời có thể tăng `commentsCount` ở post card trong cache local

---

## 11) GET `/api/Feed/posts/{id}/comments`

### Mục đích

Chỉ lấy root comments của post.

### Query params

- `cursorCreatedAt`
- `cursorId`
- `limit`

### Validation

- `postId` bắt buộc
- `cursorCreatedAt` và `cursorId` phải đi cùng nhau

### Gợi ý frontend

- chỉ dùng cho layer root comments
- mỗi comment có thể có nút `View replies (repliesCount)`
- response đã có sẵn `username`, `avatarUrl`, `isLikedByCurrentUser`, `canDelete` để render item comment trực tiếp
- không nên cố build full nested tree chỉ từ endpoint này

---

## 12) GET `/api/Feed/comments/{id}/replies`

### Mục đích

Lấy replies của một comment cụ thể.

### Query params

- `cursorCreatedAt`
- `cursorId`
- `limit`

### Validation

- `commentId` bắt buộc
- `cursorCreatedAt` và `cursorId` phải đi cùng nhau

### Gợi ý frontend

- chỉ fetch khi user expand thread hoặc bấm “xem trả lời”
- cache riêng theo `commentId`
- response đã có sẵn `username`, `avatarUrl`, `isLikedByCurrentUser`, `canDelete`
- có thể giữ cache khi collapse để mở lại nhanh hơn

---

## 13) POST `/api/Feed/comments/{id}/like`

### Mục đích

Like một comment.

### Response

- `Result<CommentLikeResponse>`

### Gợi ý frontend

- cập nhật optimistic `likesCount` và `isLikedByCurrentUser` nếu UX cần mượt
- nếu backend trả `Feed.Comment.Like.Exists`, nên đồng bộ lại state local thay vì cộng tiếp

---

## 14) DELETE `/api/Feed/comments/{id}/like`

### Mục đích

Unlike một comment.

### Response

- `Result<CommentLikeResponse>`

### Gợi ý frontend

- giảm `likesCount` và set `isLikedByCurrentUser = false` trong cache comment
- nếu backend trả `Feed.Comment.Like.NotFound`, nên coi local state đang lệch và refetch nếu cần

---

## 15) POST `/api/Feed/comments/{id}/reply`

### Request body

```json
{
  "content": "string"
}
```

### Validation

- `commentId` bắt buộc
- `content` bắt buộc
- `content` tối đa `2000` ký tự

### Gợi ý frontend

- sau reply thành công:
  - tăng `repliesCount` của parent comment trong cache
  - nếu thread đang mở, prepend hoặc invalidate replies list

---

## 16) DELETE `/api/Feed/comments/{id}`

### Mục đích

Xóa mềm một comment thread. Tác giả của comment có thể xóa comment của chính mình; chủ post cũng có thể xóa comment trong post của họ.

### Hành vi backend hiện tại

- quyền xóa hợp lệ khi `comment.canDelete === true`
- `comment.canDelete` được tính theo rule: current user là tác giả comment **hoặc** là chủ post
- delete hiện là soft-delete cả subtree bắt đầu từ comment được chọn
- nếu xóa root comment có replies thì toàn bộ replies con trong thread đó cũng bị xóa mềm

### Gợi ý frontend

- chỉ hiển thị nút xóa khi `comment.canDelete === true`
- sau delete success nên invalidate root comments và replies của thread liên quan
- nếu muốn UX an toàn hơn cho thread dài, nên confirm rõ với user rằng thao tác này có thể xóa cả replies con


## 17) POST `/api/Feed/follow/{userId}`

### Mục đích

Follow một user.

### Gợi ý frontend

- cập nhật local state của profile header nếu cần
- invalidate follow suggestions nếu UI đang hiển thị
- backend có thể trả `Feed.Follow.Self` hoặc `Feed.Follow.Exists`

---

## 18) DELETE `/api/Feed/follow/{userId}`

### Mục đích

Unfollow một user.

### Gợi ý frontend

- cập nhật local profile state và invalidate lists liên quan
- backend có thể trả `Feed.Follow.NotFound`

---

## 19) GET `/api/Feed/followers/{userId}`

### Mục đích

Lấy danh sách followers của một user.

### Response shape

Danh sách `FollowUserResponse`, hiện chỉ có:

- `userId`
- `followedAt`

### Gợi ý frontend

- nếu cần render username/avatar/fullName, frontend phải resolve profile từ nguồn khác
- endpoint hiện chưa có pagination

---

## 20) GET `/api/Feed/following/{userId}`

### Mục đích

Lấy danh sách following của một user.

### Gợi ý frontend

- cùng lưu ý như followers
- endpoint hiện chưa có pagination

---

## 21) GET `/api/Feed/follow/suggestions`

### Query params

- `limit`: optional, backend clamp `1..100`, mặc định `50`

### Mục đích

Trả danh sách account gợi ý follow cho current user.

### Hành vi backend hiện tại

- loại bỏ current user
- loại bỏ các user đã follow
- xếp hạng theo `postCount desc`, sau đó theo post gần nhất và `userId`
- response đã có `username`, `fullName`, `avatarUrl`, `postCount`

### Gợi ý frontend

- phù hợp cho sidebar hoặc onboarding suggestions
- sau follow success nên invalidate query này

---

## 22) POST `/api/Feed/reports`

### Request body

```json
{
  "targetType": "Post | Comment",
  "targetId": "guid",
  "reason": "string"
}
```

### Validation

- `targetId` bắt buộc
- `targetType` bắt buộc, tối đa `50` ký tự
- `reason` bắt buộc, tối đa `2000` ký tự
- `targetType` hợp lệ hiện chỉ là `Post` hoặc `Comment`

### Gợi ý frontend

- dùng modal report chung cho post/comment
- map `targetType` đúng casing khuyến nghị: `Post` hoặc `Comment`
- sau submit thành công chỉ cần toast xác nhận

---

## 23) GET `/api/Feed/admin/reports`

### Quyền truy cập

Yêu cầu user đã đăng nhập và thỏa policy admin.

### Query params

- `status`: optional
- `targetType`: optional

### Giá trị hợp lệ

- `status`: `Pending`, `InReview`, `Resolved`, `Dismissed`
- `targetType`: `Post`, `Comment`

### Gợi ý frontend

- chỉ dùng trong admin area
- nên guard route theo role trước khi render trang
- nếu filter không hợp lệ, backend trả lỗi business tương ứng

---

## 24) PATCH `/api/Feed/admin/reports/{id}`

### Request body

```json
{
  "status": "Pending | InReview | Resolved | Dismissed",
  "action": "None | DeleteTargetPost | null",
  "resolutionNote": "string | null"
}
```

### Validation

- `status` bắt buộc, tối đa `50`
- `action` tối đa `100` nếu có truyền
- `resolutionNote` tối đa `2000` nếu có truyền

### Hành vi moderation hiện tại

- `action = null` được normalize thành `None`
- `DeleteTargetPost` là action moderation đặc biệt hiện có
- backend kiểm tra status transition hợp lệ
- backend kiểm tra action có phù hợp với target type/status hay không

### Gợi ý frontend

- nếu chọn `DeleteTargetPost`, chỉ nên enable ở trường hợp phù hợp
- sau review success có thể update row hiện tại thay vì refetch toàn bộ danh sách

---

## Comment tree strategy cho frontend

Vì backend chia comment thành 2 lớp API:

- root comments: `/api/Feed/posts/{id}/comments`
- replies theo parent: `/api/Feed/comments/{id}/replies`

Frontend nên dùng lazy thread loading.

### Cách làm khuyến nghị

- mở post detail thì load root comments trước
- mỗi root comment hiển thị `repliesCount`
- user bấm expand mới fetch replies
- replies có pagination riêng
- không fetch toàn bộ cây comment cùng lúc

### Shape state gợi ý

```ts
interface CommentThreadState {
  rootComments: CommentResponse[];
  repliesByParentId: Record<string, CommentResponse[]>;
  expandedParentIds: Record<string, boolean>;
}
```

---

## React Query keys gợi ý

```ts
export const feedKeys = {
  all: ['feed'] as const,
  profile: (username: string) => ['feed', 'profile', username] as const,
  profilePosts: (username: string, limit: number) => ['feed', 'profile-posts', username, { limit }] as const,
  feedList: (limit: number) => ['feed', 'list', { limit }] as const,
  detail: (postId: string) => ['feed', 'detail', postId] as const,
  postComments: (postId: string) => ['feed', 'comments', postId] as const,
  commentReplies: (commentId: string) => ['feed', 'replies', commentId] as const,
  followers: (userId: string) => ['feed', 'followers', userId] as const,
  following: (userId: string) => ['feed', 'following', userId] as const,
  followSuggestions: (limit: number) => ['feed', 'follow-suggestions', { limit }] as const,
  adminReports: (filters: { status?: string; targetType?: string }) => ['feed', 'admin-reports', filters] as const,
};
```

---

## Các lỗi nghiệp vụ frontend nên map rõ

- `Feed.Post.Empty`: bài viết phải có nội dung hoặc ít nhất một media
- `Feed.Resource.Missing`: một hoặc nhiều resource không hợp lệ hoặc không resolve được
- `Feed.Post.NotFound`: bài viết không tồn tại hoặc đã bị xóa
- `Feed.Post.Like.Exists`: user đã like bài viết này rồi
- `Feed.Post.Like.NotFound`: user chưa like bài viết này
- `Feed.Comment.NotFound`: bình luận không tồn tại
- `Feed.Comment.Like.Exists`: user đã like comment này rồi
- `Feed.Comment.Like.NotFound`: user chưa like comment này
- `Feed.Follow.Self`: không thể tự follow chính mình
- `Feed.Follow.Exists`: đã follow user này rồi
- `Feed.Follow.NotFound`: chưa follow user này
- `Feed.Forbidden`: không có quyền thực hiện thao tác
- `Feed.User.NotFound`: không tìm thấy user/profile tương ứng
- `Feed.Report.Target`: loại target report không hợp lệ
- `Feed.Report.Reason`: lý do report không hợp lệ hoặc trống
- `Feed.Report.Status`: trạng thái report không hợp lệ
- `Feed.Report.Action`: action moderation không hợp lệ
- `Feed.Report.StatusTransition`: không thể chuyển report sang trạng thái đó
- `Feed.Report.ActionTarget`: action không hỗ trợ cho target hiện tại
- `Feed.Report.ActionStatus`: action yêu cầu report ở trạng thái phù hợp hơn
- `Feed.Profile.Username`: username không hợp lệ

---

## Tổng kết hành vi hệ thống hiện tại

### 1. Validation pipeline

- query pagination yêu cầu `cursorCreatedAt` và `cursorId` phải đi cùng nhau
- `limit` được normalize/clamp ở backend
- command/query chính đều đi qua FluentValidation trước khi vào handler

### 2. Cơ chế commit

- query chỉ đọc dữ liệu
- command chỉ commit khi handler trả success
- nếu handler trả `Result` failure thì transaction không được commit bởi pipeline command

### 3. Tích hợp service khác

- Feed gọi User service để resolve media/presigned URLs và profile công khai
- Feed hiện còn dùng User service để xác minh resources khi create/update post
- create post có luồng mirror sang AI service; update post hiện chưa sync AI mirror

### 4. Giới hạn hiện tại

- followers/following hiện chưa có pagination
- comment đã hỗ trợ like/unlike
- feed hiện là follow-based feed
- comment tree được load theo từng tầng, không có API trả full nested tree trong một response
