# Posts Cursor Paging

This document describes the cursor paging contract for the internal post list APIs in `Ai.Microservice`.

## Affected endpoints

- `GET /api/Ai/posts`
- `GET /api/Ai/posts/workspace/{workspaceId}`

## Query parameters

- `cursorCreatedAt`
  - Optional.
  - Type: ISO 8601 UTC datetime.
  - Use the `createdAt` value of the last item from the previous page.
- `cursorId`
  - Optional.
  - Type: GUID.
  - Use the `id` value of the last item from the previous page.
- `limit`
  - Optional.
  - Default: `50`
  - Max: `100`

`cursorCreatedAt` and `cursorId` should be sent together. If both are missing, the API returns the first page.

## Sort order

Results are sorted by:

1. `createdAt` descending
2. `id` descending

This means the newest post appears first.

## Response shape

The response body is unchanged. The API still returns `Result<IEnumerable<PostResponse>>`.

Example:

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
      "id": "9fae2c0a-b52d-4cae-a78f-b0d57d850001",
      "userId": "7f2dd31a-6df5-4b8a-97f2-5f258dd10001",
      "workspaceId": "33a3b9f0-8c51-4db1-9d9c-2d82656b0001",
      "socialMediaId": null,
      "title": "Post title",
      "content": {
        "content": "Post body",
        "hashtag": "#tag",
        "postType": "text",
        "resourceList": []
      },
      "status": "draft",
      "isPublished": false,
      "media": [],
      "publications": [],
      "createdAt": "2026-03-20T09:30:00Z",
      "updatedAt": null
    }
  ]
}
```

## Frontend paging flow

### First page

Request:

```http
GET /api/Ai/posts?limit=20
```

or

```http
GET /api/Ai/posts/workspace/{workspaceId}?limit=20
```

### Next page

Take the last item from the current page:

- `last.createdAt` -> `cursorCreatedAt`
- `last.id` -> `cursorId`

Request:

```http
GET /api/Ai/posts?limit=20&cursorCreatedAt=2026-03-20T09:30:00Z&cursorId=9fae2c0a-b52d-4cae-a78f-b0d57d850001
```

### Stop condition

- If `value.length < limit`: there is no next page.
- If `value.length === limit`: there may be another page, so frontend should allow another fetch using the last item as cursor.

This matches the existing cursor-paging style already used elsewhere in the repository.

## FE implementation notes

- Reset both cursor values when filters or workspace change.
- Keep the exact `createdAt` string returned by the API when building the next request.
- Do not build the next cursor from any item except the last item in the current page.
- If the current page is empty, stop requesting more data.
