# FR-U8 Scheduling and Auto Posting

## Muc tieu tai lieu

Tai lieu nay thay the dac ta `FR-U8` cu theo huong don gian hoa he thong AI trong `Ai.Microservice`.

Muc tieu moi:

- AI khong con la assistant dang agent manh.
- AI khong dung lich su hoi thoai lam context suy luan cho turn moi.
- AI chi xu ly duy nhat message moi nhat ma user vua gui.
- AI chi phuc vu workflow chuan bi noi dung cho scheduling.
- User van la nguoi chon thoi gian schedule va goi API schedule ro rang.

## Yeu cau nghiep vu da chot

### 1. Fixed-content scheduling van la lane chinh

He thong van ho tro:

- tao draft post;
- gan post vao schedule;
- chon thoi diem dang;
- chon social account dich;
- tu dong publish dung gio.

Scheduling duoc thuc hien qua cac API scheduling hien co theo mode `fixed_content`.

### 2. AI agent tro thanh stateless scheduling assistant

Agent chat chi con phuc vu mot tap tac vu hep:

- phan tich duy nhat message vua gui;
- kiem tra prompt co ro rang hay khong;
- neu mo ho thi tra ve `validationError` va `revisedPrompt`;
- neu da ro rang thi co the:
  - tao image task;
  - tao draft post;
  - tao draft post tu URL user dua vao;
  - search web va tao draft post tu ket qua tim duoc;
  - tao image task dong thoi tao draft post placeholder de post tu dong nhan anh sau callback;
  - link draft post voi `ChatSession` hien tai de user tiep tuc schedule sau.

Agent khong duoc:

- doc lich su chat de suy luan cho turn moi;
- doc danh sach posts/schedules de suy luan tu dong;
- tu dong chon thoi gian;
- tu dong tao `agentic schedule`;
- hoat dong nhu mot general-purpose assistant.

### 3. Validation-first contract

Khi prompt cua user chua du ro, backend phai tra ve `200 OK` theo envelope `Result<AgentChatResponse>`, trong do response co them cac field:

- `action`
- `validationError`
- `revisedPrompt`

Vi du:

User gui:

> hay tao hinh anh ve doi bong toi yeu thang giai world cup

Agent phai co kha nang tra ve:

- `action = "validation_failed"`
- `validationError = "Yeu cau chua xac dinh doi bong nao."`
- `revisedPrompt = "hay tao hinh anh ve doi bong {{ten doi bong}} thang giai world cup"`

Trong truong hop nay khong duoc tao image task, khong tao post, khong tao schedule, khong phat sinh side effect nao.

### 4. Image generation van phai ton trong coin

Neu prompt da ro rang va agent quyet dinh dung image generation:

- backend phai quote cost truoc;
- debit coin truoc khi enqueue job;
- neu khong du coin thi request that bai ngay;
- khong tao image task thanh cong neu billing fail.
- neu user muon "tao anh de dang sau", backend co the tao draft post ngay luc submit image job, sau do khi callback anh thanh cong thi draft post do phai duoc cap nhat `resource_list` bang cac resource vua sinh ra.
- draft post tao theo flow nay phai co `status = "waiting_for_image_generation"` trong luc dang doi callback anh.
- khi callback anh thanh cong, post phai chuyen ve `status = "draft"`.
- khi callback anh that bai, post phai chuyen sang `status = "image_generation_failed"`.

Billing invariant nay phai tiep tuc dung voi agent flow, giong nhu flow tao image hien co.

### 5. User la nguoi schedule

Sau khi AI da clear prompt va tao noi dung can thiet:

- user tu chon `executeAtUtc` va `timezone`;
- user goi API schedule rieng de schedule post;
- AI khong tu parse/chon thoi gian va khong tu tao schedule agentic.

## Hien trang repo tai ngay 2026-04-30

### Da co san

- `POST /api/Ai/agent/sessions/{sessionId}/messages` cho agent chat.
- `GET /api/Ai/agent/sessions/{sessionId}/messages` de lay transcript agent da persist.
- `ChatSession` de scope theo `workspace`.
- `CreateChatImageCommand` da co coin pricing + billing + enqueue image generation.
- `CreatePostCommand` / `UpdatePostCommand` da co de tao va sua draft post.
- `POST /api/Ai/schedules` va cac API schedule `fixed_content`.
- Publish flow da support `facebook`, `instagram`, `threads`, `tiktok`.

### Gioi han hien tai can ghi ro

- `Post` hien chi co mot `Content` dung chung cho cac target.
- Chua co model ben vung cho noi dung override rieng theo tung platform trong cung mot post.
- `GenerateSocialMediaCaptionsCommand` co the tao caption theo tung platform, nhung day la output generation rieng, khong phai persisted per-platform post variant.

## Quyet dinh kien truc

### 1. Khong dung transcript lam context cho agent

Agent phai hoat dong theo single-turn:

- system prompt co scope hep;
- 1 user message moi nhat;
- khong nap history tu bang `Chat` de suy luan cho turn moi.

`ChatSession` duoc giu lai de:

- xac thuc ownership;
- xac dinh `workspace`;
- link draft post hoac media workflow voi phien hien tai neu can.
- luu va doc transcript UI/UX.

### 2. Agent scheduling va web lane duoc tach ro

Lane `agentic` scheduling qua `PublishingSchedule` van khong nam trong pham vi public product flow `FR-U8` moi.

Tuy nhien, web retrieval cho `agent chat` duoc support o muc tao draft post:

- neu prompt co URL, backend fetch URL do;
- neu prompt khong co URL nhung y dinh ro rang la lay noi dung tu web, backend co the dung `web_search`;
- ket qua web co the duoc enrich, import media, roi tao draft post.

Public scheduling contract duoc chot lai:

- chi support `fixed_content`;
- `agentic` la unsupported mode;
- neu ton tai du lieu agentic cu trong DB thi xem la legacy/internal, khong phai behavior product duoc support.

### 3. Agent side effects toi gian

Neu prompt da ro rang:

- image request: agent co the goi image generation flow;
- image + post request: agent co the tao image job va tao draft post placeholder trong cung mot luot;
- text/post request: agent co the tao draft post trong workspace hien tai;
- web post request: agent co the tao draft post tu URL user dua vao hoac tu `web_search`;
- user schedule post bang API schedule rieng.

Neu prompt mo ho:

- chi tra ve huong dan sua prompt;
- khong phat sinh side effect nao.

## Tac dong API

### Agent chat

`POST /api/Ai/agent/sessions/{sessionId}/messages`

- van tra `200 OK` theo `Result<AgentChatResponse>`;
- `AgentChatResponse` duoc mo rong de FE render single-turn validation ket qua;
- response co the tra `action = "image_and_post_created"` khi backend da tao image job va draft post cung luc;
- response co the tra `action = "web_post_created"` khi backend da tao draft post tu URL hoac web search;
- request co the nhan `imageOptions` de canh hang voi UI tao anh cua FE:
  - `model`
  - `aspectRatio`
  - `resolution`
  - `numberOfVariances`
  - `socialTargets`
- route nay persist transcript vao bang `chats`, ngoai tru assistant validation reply.

Neu FE gui `imageOptions`, backend phai uu tien dung chinh cac gia tri do cho flow tao anh + tao post.
Neu FE khong gui, backend moi fallback ve config/default hien co.

#### Metadata them cho web post flow

Khi `action = "web_post_created"`, response co them:

- `retrievalMode`: `direct_url` hoac `web_search`
- `sourceUrls`: danh sach URL da duoc dung de tao draft post
- `importedResourceIds`: danh sach resource media da import vao User service

#### Mau request agent voi imageOptions

```json
{
  "message": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
  "imageOptions": {
    "model": "nano-banana-pro",
    "aspectRatio": "1:1",
    "resolution": "1K",
    "numberOfVariances": 1,
    "socialTargets": [
      {
        "platform": "instagram",
        "type": "post",
        "ratio": "1:1"
      }
    ]
  }
}
```

#### Mau response khi prompt mo ho

```json
{
  "value": {
    "sessionId": "11111111-1111-1111-1111-111111111111",
    "userMessage": {
      "id": "22222222-2222-2222-2222-222222222222",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "user",
      "content": "hay tao hinh anh ve doi bong toi yeu thang giai world cup",
      "status": null,
      "errorMessage": null,
      "model": null,
      "toolNames": [],
      "actions": [],
      "retrievalMode": null,
      "sourceUrls": [],
      "importedResourceIds": [],
      "createdAt": "2026-04-29T10:00:00Z",
      "updatedAt": null
    },
    "assistantMessage": {
      "id": "33333333-3333-3333-3333-333333333333",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "assistant",
      "content": "Yeu cau chua ro doi bong nao. Hay thay phan mo ho bang ten doi bong cu the.",
      "status": null,
      "errorMessage": null,
      "model": "gemini-3.1-flash-lite-preview",
      "toolNames": [],
      "actions": [],
      "retrievalMode": null,
      "sourceUrls": [],
      "importedResourceIds": [],
      "createdAt": "2026-04-29T10:00:00Z",
      "updatedAt": null
    },
    "action": "validation_failed",
    "validationError": "Yeu cau chua xac dinh doi bong nao.",
    "revisedPrompt": "hay tao hinh anh ve doi bong {{ten doi bong}} thang giai world cup",
    "postId": null,
    "chatId": null,
    "correlationId": null
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

#### Mau response khi tao anh + tao post thanh cong

```json
{
  "value": {
    "sessionId": "11111111-1111-1111-1111-111111111111",
    "userMessage": {
      "id": "22222222-2222-2222-2222-222222222222",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "user",
      "content": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
      "status": null,
      "errorMessage": null,
      "model": null,
      "toolNames": [],
      "actions": [],
      "retrievalMode": null,
      "sourceUrls": [],
      "importedResourceIds": [],
      "createdAt": "2026-04-29T10:05:00Z",
      "updatedAt": null
    },
    "assistantMessage": {
      "id": "33333333-3333-3333-3333-333333333333",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "assistant",
      "content": "Image generation started and a draft post was created for later scheduling.",
      "status": null,
      "errorMessage": null,
      "model": "gemini-3.1-flash-lite-preview",
      "toolNames": [
        "create_post",
        "create_chat_image"
      ],
      "actions": [
        {
          "type": "post_create",
          "toolName": "create_post",
          "status": "completed",
          "entityType": "post",
          "entityId": "44444444-4444-4444-4444-444444444444",
          "label": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
          "summary": "Draft post created and will be updated with generated images after callback.",
          "occurredAt": "2026-04-29T10:05:00Z"
        },
        {
          "type": "image_create",
          "toolName": "create_chat_image",
          "status": "completed",
          "entityType": "chat",
          "entityId": "55555555-5555-5555-5555-555555555555",
          "label": null,
          "summary": "Image generation started for the current scheduling workflow.",
          "occurredAt": "2026-04-29T10:05:01Z"
        }
      ],
      "retrievalMode": null,
      "sourceUrls": [],
      "importedResourceIds": [],
      "createdAt": "2026-04-29T10:05:01Z",
      "updatedAt": null
    },
    "action": "image_and_post_created",
    "validationError": null,
    "revisedPrompt": null,
    "postId": "44444444-4444-4444-4444-444444444444",
    "chatId": "55555555-5555-5555-5555-555555555555",
    "correlationId": "66666666-6666-6666-6666-666666666666"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

#### Mau response khi tao draft post tu URL/web search

```json
{
  "value": {
    "sessionId": "11111111-1111-1111-1111-111111111111",
    "userMessage": {
      "id": "22222222-2222-2222-2222-222222222222",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "user",
      "content": "hay tao post tu bai viet https://example.com/ai-news",
      "status": "completed",
      "errorMessage": null,
      "model": null,
      "toolNames": [],
      "actions": [],
      "retrievalMode": null,
      "sourceUrls": [],
      "importedResourceIds": [],
      "createdAt": "2026-04-30T09:00:00Z",
      "updatedAt": "2026-04-30T09:00:00Z"
    },
    "assistantMessage": {
      "id": "33333333-3333-3333-3333-333333333333",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "assistant",
      "content": "Draft post created from web content.",
      "status": "completed",
      "errorMessage": null,
      "model": "gemini-3.1-flash-lite-preview",
      "toolNames": [
        "fetch_url",
        "import_media",
        "create_post"
      ],
      "actions": [
        {
          "type": "web_post_create",
          "toolName": "direct_url",
          "status": "completed",
          "entityType": "post",
          "entityId": "44444444-4444-4444-4444-444444444444",
          "label": "AI News Roundup",
          "summary": "Draft post created from 1 web source(s).",
          "occurredAt": "2026-04-30T09:00:01Z"
        }
      ],
      "retrievalMode": "direct_url",
      "sourceUrls": [
        "https://example.com/ai-news"
      ],
      "importedResourceIds": [
        "55555555-5555-5555-5555-555555555555"
      ],
      "createdAt": "2026-04-30T09:00:01Z",
      "updatedAt": "2026-04-30T09:00:01Z"
    },
    "action": "web_post_created",
    "validationError": null,
    "revisedPrompt": null,
    "postId": "44444444-4444-4444-4444-444444444444",
    "chatId": null,
    "correlationId": null,
    "retrievalMode": "direct_url",
    "sourceUrls": [
      "https://example.com/ai-news"
    ],
    "importedResourceIds": [
      "55555555-5555-5555-5555-555555555555"
    ]
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

#### Trang thai post cua flow tao anh + tao post

FE nen render trang thai post nhu sau:

- `waiting_for_image_generation`: draft post da tao, dang doi callback anh.
- `draft`: callback anh thanh cong, `resource_list` da duoc cap nhat.
- `image_generation_failed`: callback anh that bai.

#### FE integration notes cho flow tao anh + tao post

FE nen di theo trinh tu sau:

1. Tao hoac chon `ChatSession` theo `workspace`.
2. Goi `POST /api/Ai/agent/sessions/{sessionId}/messages` voi:
   - `message`
   - `imageOptions` lay tu UI tao anh cua FE
3. Neu response la `validation_failed`:
   - hien `validationError`
   - cho user ap dung `revisedPrompt` hoac sua prompt thu cong
4. Neu response la `image_and_post_created`:
   - luu `postId`, `chatId`, `correlationId`
   - mo post detail hoac post builder dua tren `postId`
   - hien badge/trang thai `waiting_for_image_generation`
5. FE nen poll:
   - `GET /api/Ai/posts/{postId}` de theo doi `Post.status`
   - va/hoac `GET /api/Ai/chats/{chatId}` neu can render gallery anh sinh ra theo `resultResourceIds`
6. Khi `Post.status = draft`:
   - coi nhu anh da san sang trong draft post
   - mo CTA cho user chon thoi gian va target social account
7. Sau do FE goi `POST /api/Ai/schedules` voi `mode = fixed_content` de schedule post.

Khuyen nghi UX:

- Trong luc `waiting_for_image_generation`, khoa nut schedule de tranh user schedule post khi chua co media.
- Neu `image_generation_failed`, hien retry CTA o tang prompt/generation thay vi dua user vao flow schedule.
- Neu FE da co gallery/resource preview tu `chatId`, co the hien skeleton cho den khi `resultResourceIds` xuat hien.

#### End-to-end example

Vi du user muon tao anh roi len lich dang sau cho Instagram.

##### Buoc 1. Gui yeu cau toi agent

Request:

```http
POST /api/Ai/agent/sessions/{sessionId}/messages
Content-Type: application/json
Authorization: Bearer <access-token>

{
  "message": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
  "imageOptions": {
    "model": "nano-banana-pro",
    "aspectRatio": "1:1",
    "resolution": "1K",
    "numberOfVariances": 1,
    "socialTargets": [
      {
        "platform": "instagram",
        "type": "post",
        "ratio": "1:1"
      }
    ]
  }
}
```

Response:

```json
{
  "value": {
    "sessionId": "11111111-1111-1111-1111-111111111111",
    "userMessage": {
      "id": "22222222-2222-2222-2222-222222222222",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "user",
      "content": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
      "status": null,
      "errorMessage": null,
      "model": null,
      "toolNames": [],
      "actions": [],
      "createdAt": "2026-04-29T10:05:00Z",
      "updatedAt": null
    },
    "assistantMessage": {
      "id": "33333333-3333-3333-3333-333333333333",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "assistant",
      "content": "Image generation started and a draft post was created for later scheduling.",
      "status": null,
      "errorMessage": null,
      "model": "gemini-3.1-flash-lite-preview",
      "toolNames": [
        "create_post",
        "create_chat_image"
      ],
      "actions": [
        {
          "type": "post_create",
          "toolName": "create_post",
          "status": "completed",
          "entityType": "post",
          "entityId": "44444444-4444-4444-4444-444444444444",
          "label": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
          "summary": "Draft post created and will be updated with generated images after callback.",
          "occurredAt": "2026-04-29T10:05:00Z"
        },
        {
          "type": "image_create",
          "toolName": "create_chat_image",
          "status": "completed",
          "entityType": "chat",
          "entityId": "55555555-5555-5555-5555-555555555555",
          "label": null,
          "summary": "Image generation started for the current scheduling workflow.",
          "occurredAt": "2026-04-29T10:05:01Z"
        }
      ],
      "createdAt": "2026-04-29T10:05:01Z",
      "updatedAt": null
    },
    "action": "image_and_post_created",
    "validationError": null,
    "revisedPrompt": null,
    "postId": "44444444-4444-4444-4444-444444444444",
    "chatId": "55555555-5555-5555-5555-555555555555",
    "correlationId": "66666666-6666-6666-6666-666666666666"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

Luc nay FE can:

- luu `postId`
- luu `chatId`
- hien post voi `status = waiting_for_image_generation`

##### Buoc 2. Poll trang thai post

Request:

```http
GET /api/Ai/posts/{postId}
Authorization: Bearer <access-token>
```

Response khi dang doi:

```json
{
  "value": {
    "id": "44444444-4444-4444-4444-444444444444",
    "workspaceId": "77777777-7777-7777-7777-777777777777",
    "chatSessionId": "11111111-1111-1111-1111-111111111111",
    "title": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
    "content": {
      "content": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
      "hashtag": null,
      "resourceList": [],
      "postType": "posts"
    },
    "status": "waiting_for_image_generation"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

##### Buoc 3. Sau callback anh thanh cong

Request:

```http
GET /api/Ai/posts/{postId}
Authorization: Bearer <access-token>
```

Response:

```json
{
  "value": {
    "id": "44444444-4444-4444-4444-444444444444",
    "workspaceId": "77777777-7777-7777-7777-777777777777",
    "chatSessionId": "11111111-1111-1111-1111-111111111111",
    "title": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
    "content": {
      "content": "hay tao anh de toi dang sau ve doi tuyen Argentina vo dich World Cup",
      "hashtag": null,
      "resourceList": [
        "88888888-8888-8888-8888-888888888888"
      ],
      "postType": "posts"
    },
    "status": "draft"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

Luc nay FE co the mo CTA schedule.

##### Buoc 4. Tao fixed-content schedule

Request:

```http
POST /api/Ai/schedules
Content-Type: application/json
Authorization: Bearer <access-token>

{
  "workspaceId": "77777777-7777-7777-7777-777777777777",
  "name": "Argentina Instagram post",
  "mode": "fixed_content",
  "executeAtUtc": "2026-04-29T14:00:00Z",
  "timezone": "Asia/Ho_Chi_Minh",
  "isPrivate": false,
  "items": [
    {
      "itemType": "post",
      "itemId": "44444444-4444-4444-4444-444444444444",
      "sortOrder": 1,
      "executionBehavior": "publish_all"
    }
  ],
  "targets": [
    {
      "socialMediaId": "99999999-9999-9999-9999-999999999999",
      "isPrimary": true
    }
  ]
}
```

Response:

```json
{
  "value": {
    "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "name": "Argentina Instagram post",
    "mode": "fixed_content",
    "status": "scheduled"
  },
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

Ket qua:

- agent chi giup tao noi dung
- post cho biet ro dang doi anh hay da san sang
- user van la nguoi chot thoi gian schedule
- schedule van di qua lane `fixed_content`

`GET /api/Ai/agent/sessions/{sessionId}/messages`

- la contract duoc support de FE doc transcript agent da persist;
- transcript duoc lay tu bang `chats`;
- transcript chi phuc vu UI/UX va audit turn, khong duoc nap nguoc lai lam context suy luan cho agent;
- assistant message co `action = "validation_failed"` khong duoc luu vao transcript;
- transcript duoc sap xep theo `CreatedAt`, sau do theo `Id`.

#### Mau response transcript

```json
{
  "value": [
    {
      "id": "22222222-2222-2222-2222-222222222222",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "user",
      "content": "hay tao post tu bai viet https://example.com/ai-news",
      "status": "completed",
      "errorMessage": null,
      "model": null,
      "toolNames": [],
      "actions": [],
      "retrievalMode": null,
      "sourceUrls": [],
      "importedResourceIds": [],
      "createdAt": "2026-04-30T09:00:00Z",
      "updatedAt": "2026-04-30T09:00:00Z"
    },
    {
      "id": "33333333-3333-3333-3333-333333333333",
      "sessionId": "11111111-1111-1111-1111-111111111111",
      "role": "assistant",
      "content": "Draft post created from web content.",
      "status": "completed",
      "errorMessage": null,
      "model": "gemini-3.1-flash-lite-preview",
      "toolNames": [
        "fetch_url",
        "import_media",
        "create_post"
      ],
      "actions": [
        {
          "type": "web_post_create",
          "toolName": "direct_url",
          "status": "completed",
          "entityType": "post",
          "entityId": "44444444-4444-4444-4444-444444444444",
          "label": "AI News Roundup",
          "summary": "Draft post created from 1 web source(s).",
          "occurredAt": "2026-04-30T09:00:01Z"
        }
      ],
      "retrievalMode": "direct_url",
      "sourceUrls": [
        "https://example.com/ai-news"
      ],
      "importedResourceIds": [
        "55555555-5555-5555-5555-555555555555"
      ],
      "createdAt": "2026-04-30T09:00:01Z",
      "updatedAt": "2026-04-30T09:00:01Z"
    }
  ],
  "isSuccess": true,
  "isFailure": false,
  "error": {
    "code": "",
    "description": ""
  }
}
```

### Scheduling

`/api/Ai/schedules`

- chi support `fixed_content`;
- request `mode=agentic` phai bi tu choi ro rang.

## Tieu chi chap nhan

- Agent khong doc history tu `Chat`.
- Agent co the tao draft post tu URL hoac web search.
- Agent luu transcript vao bang `chats`.
- Assistant validation reply khong duoc luu vao transcript.
- Prompt mo ho tra `validationError` + `revisedPrompt`.
- Prompt da ro cho image phai ton trong coin.
- Prompt da ro cho post phai tao duoc draft post trong workspace cua session.
- Prompt da ro cho URL/web search phai tao duoc draft post va, neu media hop le, co the import media vao User resources.
- Prompt da ro cho image dang sau phai tao duoc draft post va tu dong cap nhat `resource_list` cua draft post sau khi image callback thanh cong.
- FE co the biet post dang doi anh qua `Post.status = "waiting_for_image_generation"`.
- API scheduling van chay cho `fixed_content`.
- `agentic` scheduling bi khoa o public flow.

## Ghi chu cho implementer

- Uu tien giu backward compatibility toi da cho envelope `Result`.
- Khong mo rong pham vi sang per-platform post override trong change nay.
- Khong chinh sua publish flow hien co, ngoai viec lam ro trong docs rang 1 post hien chi co 1 `Content` dung chung.
