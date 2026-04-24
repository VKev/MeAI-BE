# FR-A5 Frontend API Key Management Integration

## Mục tiêu

Tài liệu này hướng dẫn frontend triển khai màn hình quản lý API key cho `FR-A5` dựa trên backend đã có trong repo hiện tại.

Phạm vi:

- trang admin để xem danh sách API key của từng service;
- form thêm mới key;
- form chỉnh sửa key;
- bật/tắt `isActive`;
- hiển thị trạng thái masked secret và metadata;
- dùng đúng contract hiện có của backend, không bịa thêm field hay route.

Tài liệu requirement gốc:

- [docs/FR-A5-API-key-management.md](/home/vinhdo/Documents/GitHub/MeAI-BE/docs/FR-A5-API-key-management.md)

## 1. Tổng quan FE flow

Frontend nên xem `FR-A5` là một admin module có 2 nhóm dữ liệu tách riêng:

- `User.Microservice` credentials
- `Ai.Microservice` credentials

UI đơn giản nhất:

1. admin chọn service cần quản lý;
2. FE gọi API list của service đó;
3. FE render bảng key hiện có;
4. admin có thể mở modal hoặc drawer để:
   - thêm key mới
   - chỉnh sửa key hiện có
   - bật/tắt key
5. sau khi save thành công, FE refetch danh sách để đồng bộ state.

Khuyến nghị:

- không giữ local optimistic state quá lâu cho secret data;
- sau mỗi thao tác create/update nên refetch;
- không cache lâu ở browser cho trang này.

## 2. Backend APIs thực tế

## 2.1. User service

Base route:

- `GET /api/User/admin/api-keys`
- `POST /api/User/admin/api-keys`
- `PUT /api/User/admin/api-keys/{id}`

Source:

- `Backend/Microservices/User.Microservice/src/WebApi/Controllers/AdminApiKeysController.cs`

## 2.2. AI service

Base route:

- `GET /api/Ai/admin/api-keys`
- `POST /api/Ai/admin/api-keys`
- `PUT /api/Ai/admin/api-keys/{id}`

Source:

- `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/AdminApiKeysController.cs`

## 2.3. Gateway usage

Frontend nên gọi qua API Gateway theo pattern routing hiện có của hệ thống.

Nếu frontend đang dùng cùng base URL gateway như các module khác, chỉ cần đổi path:

- `/api/User/admin/api-keys`
- `/api/Ai/admin/api-keys`

## 3. Authentication và authorization

Các endpoint này là `admin-only`.

Role backend đang chấp nhận:

- `User.Microservice`: `ADMIN`, `Admin`
- `Ai.Microservice`: `ADMIN`, `Admin`, `admin`

Kỳ vọng FE:

- chỉ hiển thị menu/trang này cho admin;
- nếu backend trả `401`, điều hướng về login hoặc refresh session theo flow hiện có;
- nếu backend trả `403`, hiển thị thông báo không đủ quyền;
- không cố che lỗi quyền bằng UI fallback mơ hồ.

## 4. Response contract

Backend đang trả `Result<T>` giống các admin API khác trong hệ thống.

Response item hiện có:

```ts
export type ApiCredentialItem = {
  id: string;
  serviceName: "User" | "Ai";
  provider: string;
  keyName: string;
  displayName: string;
  maskedValue: string;
  isActive: boolean;
  source: string;
  version: number;
  lastSyncedFromEnvAt: string | null;
  lastRotatedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
};

export type Result<T> = {
  isSuccess: boolean;
  isFailure: boolean;
  error: {
    code: string;
    description: string;
  };
  value: T;
};
```

Lưu ý:

- `maskedValue` chỉ là giá trị che một phần, FE không có raw secret cũ để hiển thị lại;
- đây là hành vi đúng, không phải bug;
- khi edit một key hiện có, ô nhập secret phải để trống mặc định.

## 5. Query params của API list

`GET` hiện hỗ trợ:

- `provider?: string`
- `isActive?: boolean`
- `keyName?: string`

Ví dụ:

- `/api/User/admin/api-keys?provider=Stripe`
- `/api/Ai/admin/api-keys?isActive=true`
- `/api/Ai/admin/api-keys?provider=Gemini&keyName=ApiKey`

FE nên map các filter này thành:

- dropdown `Provider`
- toggle hoặc select `Active / Inactive / All`
- search input cho `Key Name`

## 6. Request payloads

## 6.1. Tạo key mới

Payload `POST`:

```ts
export type CreateApiCredentialRequest = {
  provider: string;
  keyName: string;
  displayName?: string | null;
  value: string;
  isActive?: boolean;
};
```

Ví dụ:

```json
{
  "provider": "Gemini",
  "keyName": "ApiKey",
  "displayName": "Gemini production key",
  "value": "AIza....",
  "isActive": true
}
```

## 6.2. Chỉnh sửa key

Payload `PUT`:

```ts
export type UpdateApiCredentialRequest = {
  displayName?: string | null;
  value?: string | null;
  isActive?: boolean | null;
};
```

Ví dụ chỉ đổi tên:

```json
{
  "displayName": "Gemini primary key"
}
```

Ví dụ rotate secret:

```json
{
  "value": "AIza....new"
}
```

Ví dụ deactivate:

```json
{
  "isActive": false
}
```

## 7. Gợi ý UI structure

## 7.1. Layout đề xuất

Một layout đủ dùng:

- tabs hoặc segmented control để chuyển giữa `User` và `Ai`
- filter row phía trên bảng
- data table danh sách keys
- nút `Add key`
- nút `Edit` trên từng row
- status badge cho `isActive`
- metadata phụ cho `source`, `version`, `updatedAt`

## 7.2. Columns đề xuất

- `Provider`
- `Key Name`
- `Display Name`
- `Masked Value`
- `Status`
- `Source`
- `Version`
- `Last Rotated`
- `Last Synced From Env`
- `Updated At`
- `Actions`

## 7.3. Row actions

- `Edit`
- `Activate / Deactivate`

Hiện backend chưa có endpoint `rotate` riêng, nên FE có thể:

- dùng cùng form edit;
- nếu admin nhập `value` mới thì xem đó là rotate.

## 8. Form behavior bắt buộc

## 8.1. Create form

Field nên có:

- `provider`
- `keyName`
- `displayName`
- `value`
- `isActive`

Validation FE tối thiểu:

- `provider` bắt buộc
- `keyName` bắt buộc
- `value` bắt buộc

## 8.2. Edit form

Field nên có:

- `displayName`
- `value`
- `isActive`

Rule:

- `value` để trống mặc định;
- nếu admin không nhập `value`, FE chỉ gửi field cần update khác;
- không đổ `maskedValue` ngược vào input secret;
- nếu admin paste secret mới thì FE gửi `value` mới.

## 8.3. UX copy gợi ý

- label input secret mới: `New secret value`
- helper text: `Leave empty if you do not want to rotate this key`
- helper text trạng thái: `Inactive keys will not be used by runtime flows`

## 9. Rendering states

Frontend nên xử lý rõ các state sau:

- `loading`
- `empty`
- `error`
- `saving`
- `save success`

Ví dụ:

- loading table skeleton khi gọi list;
- empty state riêng nếu service chưa có key nào;
- inline error từ `ProblemDetails.detail` khi create/update fail;
- disable submit button trong lúc save.

## 10. Error handling

Backend đang trả lỗi business qua `ProblemDetails`.

FE nên đọc:

- `status`
- `type`
- `detail`
- `errors`

Một số case thực tế:

- `ApiCredential.InvalidRequest`
- `ApiCredential.AlreadyExists`
- `ApiCredential.NotFound`

Mapping UI gợi ý:

- `AlreadyExists` -> báo trùng key, giữ form mở;
- `InvalidRequest` -> highlight field tương ứng nếu xác định được;
- `NotFound` -> đóng modal và refetch list;
- `401/403` -> xử lý theo auth flow admin.

## 11. TypeScript client gợi ý

```ts
export type ServiceKind = "User" | "Ai";

export function getApiKeyPath(service: ServiceKind) {
  return service === "User"
    ? "/api/User/admin/api-keys"
    : "/api/Ai/admin/api-keys";
}

export async function listApiKeys(
  service: ServiceKind,
  query?: {
    provider?: string;
    isActive?: boolean;
    keyName?: string;
  }
) {
  const params = new URLSearchParams();

  if (query?.provider) params.set("provider", query.provider);
  if (query?.isActive !== undefined) params.set("isActive", String(query.isActive));
  if (query?.keyName) params.set("keyName", query.keyName);

  const url = `${getApiKeyPath(service)}${params.size > 0 ? `?${params}` : ""}`;
  const response = await fetch(url, {
    method: "GET",
    credentials: "include"
  });

  if (!response.ok) {
    throw await response.json();
  }

  return response.json() as Promise<Result<ApiCredentialItem[]>>;
}

export async function createApiKey(
  service: ServiceKind,
  payload: CreateApiCredentialRequest
) {
  const response = await fetch(getApiKeyPath(service), {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw await response.json();
  }

  return response.json() as Promise<Result<ApiCredentialItem>>;
}

export async function updateApiKey(
  service: ServiceKind,
  id: string,
  payload: UpdateApiCredentialRequest
) {
  const response = await fetch(`${getApiKeyPath(service)}/${id}`, {
    method: "PUT",
    credentials: "include",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw await response.json();
  }

  return response.json() as Promise<Result<ApiCredentialItem>>;
}
```

## 12. React state gợi ý

Nếu dùng React Query hoặc SWR:

- query key: `["admin-api-keys", service, filters]`
- mutation create xong: invalidate query list của service tương ứng
- mutation update xong: invalidate query list của service tương ứng

Nếu dùng state thường:

- save thành công thì gọi lại API list;
- không nên tự merge local bằng giả định vì backend có thể tăng `version`, đổi `source`, hoặc cập nhật `updatedAt`.

## 13. Danh sách provider nên chuẩn bị trên FE

## 13.1. User service

Provider hiện backend đã seed/sync:

- `Stripe`
- `Facebook`
- `Instagram`
- `TikTok`
- `Threads`

## 13.2. AI service

Provider hiện backend đã seed/sync:

- `Gemini`
- `Kie`
- `N8n`

Khuyến nghị:

- FE không hardcode quá cứng danh sách provider trong logic;
- có thể hardcode cho dropdown ban đầu, nhưng vẫn phải render được item lạ từ backend nếu sau này có provider mới.

## 14. Những điều FE không nên làm

- Không hiển thị lại raw secret cũ.
- Không dùng `maskedValue` làm giá trị edit.
- Không assume `serviceName` luôn trùng tab đang mở nếu tái sử dụng component.
- Không swallow `403` thành empty state.
- Không lưu secret draft vào URL query string.
- Không log secret ở console, analytics, hoặc error tracking.

## 15. Checklist hoàn thành frontend

Frontend được coi là xong phần `FR-A5` khi:

1. Admin xem được list API key của `User` và `Ai`.
2. Admin filter được theo `provider`, `keyName`, `isActive`.
3. Admin thêm mới một key qua form.
4. Admin chỉnh sửa metadata hoặc secret của một key qua form.
5. Admin bật/tắt `isActive`.
6. UI không bao giờ hiển thị raw secret đã lưu trước đó.
7. UI xử lý đúng `401`, `403`, business errors, loading state, và refetch sau save.

## 16. Gợi ý mở rộng sau này

Nếu backend bổ sung thêm các tính năng sau, FE có thể mở rộng tiếp:

- endpoint `rotate` riêng;
- audit trail lịch sử đổi key;
- detail drawer cho từng version;
- confirm dialog khi deactivate key đang active;
- badge cảnh báo key vừa bị env sync ghi đè ở lần startup gần nhất.
