# MeAI n8n workflows for FR-U8

Thư mục này chứa 2 workflow phục vụ `FR-U8`:

- `workflows/meai-scheduled-agent-job.json`
  - nhận request đăng ký agentic schedule từ `Ai.Microservice`;
  - trả `ack` ngay;
  - đợi tới `executeAtUtc`;
  - gọi Brave Search;
  - callback kết quả về `Ai.Microservice`.
- `workflows/meai-web-search.json`
  - nhận request `web_search` trực tiếp từ Gemini tool;
  - gọi Brave Search;
  - trả normalized payload về `Ai.Microservice`.

## Cách dùng local

1. Chạy stack compose có `n8n`.
2. `docker compose` sẽ chạy service `n8n-import` để import toàn bộ file JSON trong `workflows/` trước khi `n8n` start.
3. Mở `http://localhost:5678/n8n/` để kiểm tra chúng đã xuất hiện.
4. Đảm bảo `Ai.Microservice` và `n8n` dùng cùng callback token.
5. Nếu bạn thay đổi file JSON workflow, hãy rerun `n8n-import` hoặc recreate stack để import lại.

## Env vars cần có

- `BRAVE_SEARCH_API_KEY`
  - được `n8n` dùng để gọi Brave Search API qua header `X-Subscription-Token`.
- `N8n__BaseUrl`
- `N8n__ScheduledAgentJobPath`
- `N8n__WebSearchPath`
- `N8n__CallbackBaseUrl`
- `N8n__RuntimeCallbackPath`
- `N8n__InternalCallbackToken`

## Payload contract

### Scheduled agent job webhook

Request body từ `Ai.Microservice`:

```json
{
  "jobId": "guid",
  "scheduleId": "guid",
  "userId": "guid",
  "workspaceId": "guid",
  "executeAtUtc": "2026-04-23T10:00:00Z",
  "timezone": "Asia/Ho_Chi_Minh",
  "search": {
    "queryTemplate": "kết quả xổ số miền bắc hôm nay",
    "country": "VN",
    "searchLang": "vi",
    "count": 5,
    "freshness": "pd"
  },
  "callback": {
    "url": "http://api-gateway:8080/api/Ai/internal/agent-schedules/runtime-result",
    "token": "local-agent-schedule-callback-token"
  }
}
```

Response body kỳ vọng:

```json
{
  "executionId": "n8n-execution-id",
  "acceptedAtUtc": "2026-04-23T09:30:00Z"
}
```

### Direct web_search webhook

Request body:

```json
{
  "queryTemplate": "kết quả xổ số miền bắc hôm nay",
  "count": 5,
  "country": "VN",
  "searchLang": "vi",
  "freshness": "pd",
  "timezone": "Asia/Ho_Chi_Minh",
  "executeAtUtc": "2026-04-23T10:00:00Z"
}
```

Response body:

```json
{
  "query": "kết quả xổ số miền bắc hôm nay",
  "retrievedAtUtc": "2026-04-23T10:00:00Z",
  "results": [
    {
      "title": "Example",
      "url": "https://example.com",
      "description": "..."
    }
  ],
  "llmContext": "string"
}
```

## Ghi chú

- Các workflow này cố tình giữ `Ai.Microservice` là source of truth.
- `n8n` chỉ orchestration + web fetching, không publish social trực tiếp.
- Compose hiện dùng service `n8n-import` với lệnh `import:workflow --separate --input=/workspace/n8n-local/workflows` để auto-import workflow trước khi `n8n` start.
- Nếu import workflow mà `n8n` version khác nhau làm lệch một vài node option nhỏ, hãy giữ nguyên shape và contract của payload ở trên.
