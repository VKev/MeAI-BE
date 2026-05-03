# AI Enhance Existing Posts

## Endpoint

- `POST /api/Ai/posts/{postId}/enhance`

## Request Contract

```json
{
  "platform": "instagram",
  "resourceIds": ["uuid"],
  "language": "vi",
  "instruction": "giọng thân thiện, ngắn gọn",
  "suggestionCount": 3
}
```

### Request Notes

- `platform` is required.
- `suggestionCount` defaults to `3` when omitted.
- `suggestionCount` is clamped to a maximum of `6`.
- `resourceIds` is optional.

## Response Contract

Success responses preserve the existing `Result<T>` envelope:

```json
{
  "isSuccess": true,
  "value": {
    "postId": "uuid",
    "platform": "ig",
    "resourceIds": ["uuid"],
    "bestSuggestion": {
      "caption": "...",
      "hashtags": ["#meai"],
      "trendingHashtags": ["#marketing"],
      "callToAction": "..."
    },
    "alternatives": [
      {
        "caption": "...",
        "hashtags": [],
        "trendingHashtags": [],
        "callToAction": "..."
      }
    ]
  }
}
```

### Response Notes

- `bestSuggestion` is always the first suggestion returned by the AI model.
- `alternatives` contains the remaining suggestions.
- When only one suggestion is requested, `alternatives` is an empty array.
- `platform` is normalized to the existing internal platform values (`facebook`, `tiktok`, `ig`, `threads`).

## Resource Context Resolution

Resource context is resolved in this order:

1. Request `resourceIds` override, if provided and non-empty.
2. Existing post `content.resource_list`, if the request omits `resourceIds`.
3. Text-only enhancement using the post title/content when no resources are available.

This means v1 still supports enhancement for posts without media.

## Billing And Refund Behavior

- Billing uses the new action type: `post_enhancement`.
- Pricing follows the same default per-platform model as caption generation.
- Coins are debited before the AI call starts.
- If the AI call fails after debit, the system refunds the full amount exactly once.
- Spend records are tracked separately from caption batch generation so admin/user usage views can distinguish the feature.

## Validation And Errors

Validation and business errors preserve the existing `ProblemDetails` response flow.

Expected errors include:

- `Post.NotFound`
- `Post.Unauthorized`
- `SocialMedia.InvalidType`
- `Billing.InsufficientFunds`

Additional request validation:

- Missing request body returns `Post.EnhanceInvalidRequest`.
- Missing/blank platform returns `SocialMedia.InvalidType`.

## Out Of Scope For V1

The following capabilities are intentionally not included:

- Re-generate media
- Reframe image
- Suggest schedule
- Multi-step optimization suite
- Auto-writing suggestions back into the stored post

## Frontend Integration Guidance

Frontend should treat this endpoint as a suggestion generator only:

1. Call the enhance endpoint for the target post.
2. Show `bestSuggestion` first and optionally list `alternatives`.
3. Let the user choose a suggestion.
4. Apply the selected fields into the existing draft/post edit flow already used for post updates.
5. Persist the chosen text through the normal post update endpoint.

The enhance endpoint does not mutate the stored post by itself.
