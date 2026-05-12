# AI Get All Posts API

## Trạng thái triển khai

Tài liệu này mô tả contract backend hiện tại của API lấy danh sách post trong `Ai.Microservice`.

### API đã triển khai

- [x] `GET /api/Ai/posts`

### Contract hiện tại

- [x] API yêu cầu xác thực.
- [x] Không có request body.
- [x] Hỗ trợ cursor pagination qua `cursorCreatedAt` và `cursorId`.
- [x] Hỗ trợ filter theo `status`.
- [x] Hỗ trợ filter theo account qua `socialMediaId` hoặc alias `accountId`.
- [x] Hỗ trợ filter theo social platform qua `platform` hoặc alias `social`.
- [x] Response success dùng envelope `Result<IEnumerable<PostResponse>>`.
- [x] Nếu không có user đăng nhập, API trả `401` với shape `{ "message": "Unauthorized" }`.

## Mục tiêu

API này trả danh sách post của user hiện tại trong Ai service. Endpoint được dùng cho màn hình danh sách post tổng, và hiện đã cho phép FE lọc theo account đã connect và theo nền tảng social.

## Endpoint

`GET /api/Ai/posts`

## Authentication

- Yêu cầu user đã đăng nhập.
- Đi qua API Gateway thì route đầy đủ thường là `http://localhost:2406/api/Ai/posts`.
- Có thể dùng cookie auth hoặc bearer token tùy flow hiện tại của FE.

## Request model

API này không nhận JSON body. Tất cả filter đi qua query string.

### Query parameters

| Name | Type | Required | Notes |
| --- | --- | --- | --- |
| `cursorCreatedAt` | ISO-8601 datetime | No | Dùng cho cursor pagination. Nên truyền cùng `cursorId`. |
| `cursorId` | uuid | No | Dùng cho cursor pagination. Nên truyền cùng `cursorCreatedAt`. |
| `limit` | int | No | Mặc định `50`, tối đa `100`. |
| `status` | string | No | Lọc theo trạng thái post, ví dụ `draft`, `scheduled`, `processing`, `failed`. |
| `socialMediaId` | uuid | No | Filter theo account social đã connect. |
| `accountId` | uuid | No | Alias của `socialMediaId`. Nếu truyền cả hai thì backend ưu tiên `socialMediaId`. |
| `platform` | string | No | Filter theo social platform. |
| `social` | string | No | Alias của `platform`. Nếu truyền cả hai thì backend ưu tiên `platform`. |

### Platform values

Backend xử lý không phân biệt hoa thường. Các giá trị alias đang được hỗ trợ:

- `facebook` hoặc `fb`
- `instagram` hoặc `ig`
- `threads` hoặc `thread`
- `tiktok`

Nếu truyền một giá trị khác các alias trên, backend sẽ filter theo đúng chuỗi đã truyền trong cột `post.platform`.

### Request examples

Lấy toàn bộ post của user hiện tại:

```bash
curl -X GET "http://localhost:2406/api/Ai/posts" \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

Lọc theo account:

```bash
curl -X GET "http://localhost:2406/api/Ai/posts?accountId=11111111-1111-1111-1111-111111111111" \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

Lọc theo social:

```bash
curl -X GET "http://localhost:2406/api/Ai/posts?social=facebook" \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

Lọc kết hợp account + social + status:

```bash
curl -X GET "http://localhost:2406/api/Ai/posts?socialMediaId=11111111-1111-1111-1111-111111111111&platform=facebook&status=draft&limit=20" \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

Lấy trang tiếp theo bằng cursor:

```bash
curl -X GET "http://localhost:2406/api/Ai/posts?cursorCreatedAt=2026-05-04T10:30:00Z&cursorId=22222222-2222-2222-2222-222222222222&limit=20" \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

## Response model

### Success response

HTTP `200 OK`

Envelope:

```json
{
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  },
  "value": [
    {
      "id": "22222222-2222-2222-2222-222222222222",
      "userId": "33333333-3333-3333-3333-333333333333",
      "username": "meai_user",
      "avatarUrl": "https://cdn.example.com/avatar.jpg",
      "workspaceId": "44444444-4444-4444-4444-444444444444",
      "postBuilderId": "55555555-5555-5555-5555-555555555555",
      "chatSessionId": null,
      "socialMediaId": "11111111-1111-1111-1111-111111111111",
      "platform": "facebook",
      "title": "Summer launch post",
      "content": {
        "content": "Launching our summer campaign this week.",
        "hashtag": "#summer #launch",
        "resource_list": [
          "66666666-6666-6666-6666-666666666666"
        ],
        "post_type": "posts"
      },
      "status": "draft",
      "schedule": {
        "scheduleGroupId": "77777777-7777-7777-7777-777777777777",
        "scheduledAtUtc": "2026-05-05T02:00:00Z",
        "timezone": "Asia/Ho_Chi_Minh",
        "socialMediaIds": [
          "11111111-1111-1111-1111-111111111111"
        ],
        "isPrivate": false
      },
      "isPublished": false,
      "media": [
        {
          "resourceId": "66666666-6666-6666-6666-666666666666",
          "presignedUrl": "https://cdn.example.com/resource.jpg",
          "contentType": "image/jpeg",
          "resourceType": "image"
        }
      ],
      "publications": [
        {
          "id": "88888888-8888-8888-8888-888888888888",
          "socialMediaId": "11111111-1111-1111-1111-111111111111",
          "socialMediaType": "facebook",
          "destinationOwnerId": "1234567890",
          "externalContentId": "123456789012345",
          "externalContentIdType": "post",
          "contentType": "posts",
          "publishStatus": "published",
          "publishedAt": "2026-05-03T12:00:00Z",
          "createdAt": "2026-05-03T11:59:00Z"
        }
      ],
      "createdAt": "2026-05-03T11:50:00Z",
      "updatedAt": "2026-05-03T12:10:00Z"
    }
  ]
}
```

### `PostResponse` fields

| Field | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `id` | uuid | No | Post id. |
| `userId` | uuid | No | Owner user id. |
| `username` | string | No | Username của owner. |
| `avatarUrl` | string | Yes | Avatar public của owner. |
| `workspaceId` | uuid | Yes | Workspace gắn với post nếu có. |
| `postBuilderId` | uuid | Yes | Nhóm post builder nếu post thuộc builder flow. |
| `chatSessionId` | uuid | Yes | Chat session liên quan nếu có. |
| `socialMediaId` | uuid | Yes | Account social đang gắn với post. |
| `platform` | string | Yes | Nền tảng nháp/target của post, ví dụ `facebook`, `instagram`, `tiktok`, `threads`. |
| `title` | string | Yes | Tiêu đề post. |
| `content` | object | Yes | Nội dung post. Xem `PostContent`. |
| `status` | string | Yes | Trạng thái post hiện tại. |
| `schedule` | object | Yes | Thông tin schedule nếu post đang có lịch. |
| `isPublished` | bool | No | `true` nếu có ít nhất một publication ở trạng thái `published`. |
| `media` | array | No | Danh sách media đã được presign URL. |
| `publications` | array | No | Danh sách publication theo từng destination đã publish. |
| `createdAt` | datetime | Yes | Thời điểm tạo. |
| `updatedAt` | datetime | Yes | Thời điểm cập nhật cuối. |

### `PostContent` fields

`content` dùng JSON field name hỗn hợp theo contract hiện tại:

| Field | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `content` | string | Yes | Main caption/text. |
| `hashtag` | string | Yes | Raw hashtag string. |
| `resource_list` | string[] | Yes | Danh sách resource id dạng string uuid. |
| `post_type` | string | Yes | Thường là `posts` hoặc `reels`. |

### `schedule` fields

| Field | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `scheduleGroupId` | uuid | No | Nhóm schedule hiện tại. |
| `scheduledAtUtc` | datetime | No | Thời điểm chạy UTC. |
| `timezone` | string | Yes | Timezone FE dùng để hiển thị. |
| `socialMediaIds` | uuid[] | No | Danh sách account sẽ publish khi tới lịch. |
| `isPrivate` | bool | Yes | Cờ privacy cho các platform hỗ trợ. |

### `media` item fields

| Field | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `resourceId` | uuid | No | Resource id. |
| `presignedUrl` | string | No | URL signed để FE preview/download. |
| `contentType` | string | Yes | MIME type. |
| `resourceType` | string | Yes | Kiểu resource, ví dụ `image`, `video`. |

### `publications` item fields

| Field | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `id` | uuid | No | Publication row id. |
| `socialMediaId` | uuid | No | Account đã publish. |
| `socialMediaType` | string | No | Platform của publication, ví dụ `facebook`. |
| `destinationOwnerId` | string | No | Id page/account/channel đích trên platform. |
| `externalContentId` | string | No | Id bài viết/video trên platform. |
| `externalContentIdType` | string | No | Kiểu id external, ví dụ `post`. |
| `contentType` | string | No | Loại nội dung publish, ví dụ `posts`, `reels`. |
| `publishStatus` | string | No | Trạng thái publish hiện tại. |
| `publishedAt` | datetime | Yes | Thời điểm publish thành công nếu có. |
| `createdAt` | datetime | No | Thời điểm tạo publication row. |

## Error responses

### Unauthorized

HTTP `401 Unauthorized`

```json
{
  "message": "Unauthorized"
}
```

## Backend notes

- Nếu không truyền filter, API trả toàn bộ post chưa bị soft-delete của user hiện tại.
- Filter account dùng `socialMediaId` trong bảng post.
- Filter social dùng cột `platform`.
- `socialMediaId` ưu tiên hơn `accountId`.
- `platform` ưu tiên hơn `social`.
- Pagination hiện là cursor theo `createdAt desc, id desc`.
