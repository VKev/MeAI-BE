# AI spending time

## Exposed timing fields

`AiUsageHistoryItemResponse` now includes these nullable fields on existing AI usage history responses:

- `startedAtUtc`
- `completedAtUtc`
- `processingDurationSeconds`

## Timing resolution rules

### Image generation

For records where `referenceType = "chat_image"`:

1. Parse `referenceId` as `Chat.Id`.
2. Load the matching `Chat` row in batch.
3. Read `Chat.Config` and parse `CorrelationId` / `correlationId`.
4. Batch load `ImageTask` rows by correlation id.
5. Map:
   - `startedAtUtc = ImageTask.CreatedAt`
   - `completedAtUtc = ImageTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` when `completedAtUtc` exists and is later than `startedAtUtc`.

### Video generation

For records where `referenceType = "chat_video"`:

1. Parse `referenceId` as `Chat.Id`.
2. Load the matching `Chat` row in batch.
3. Read `Chat.Config` and parse `CorrelationId` / `correlationId`.
4. Batch load `VideoTask` rows by correlation id.
5. Map:
   - `startedAtUtc = VideoTask.CreatedAt`
   - `completedAtUtc = VideoTask.CompletedAt`
   - `processingDurationSeconds = floor((completedAtUtc - startedAtUtc).TotalSeconds)` when `completedAtUtc` exists and is later than `startedAtUtc`.

## Why caption generation has no timing in v1

Caption generation still returns `null` for all three timing fields in v1 because that flow does not currently have a dedicated task entity with standardized started/completed timestamps that can be joined reliably from read side history records.

## Null return rules

All three timing fields are returned as `null` when:

- the usage record reference type is unsupported for timing enrichment
- `referenceId` cannot be parsed as a chat id
- the chat row is missing
- `Chat.Config` is missing or invalid JSON
- the correlation id cannot be parsed from chat config
- the related image/video task row cannot be found

For incomplete tasks:

- `startedAtUtc` is returned from the task row
- `completedAtUtc` is `null`
- `processingDurationSeconds` is `null`

## Performance note

History enrichment is resolved in batches per page to avoid N+1 queries:

1. materialize the spend record page
2. group supported records by reference type
3. batch load chats by chat ids
4. extract correlation ids in memory
5. batch load image tasks and video tasks by correlation ids
6. map timing back onto response items in memory

## Known limitations

- This is a read-side join only; timing is not persisted to `AiSpendRecord` in v1.
- If chat config is changed after record creation, the join depends on the current stored config content.
- The join relies on `referenceId` pointing to `Chat.Id` instead of directly storing task correlation ids.
- Caption generation remains unresolved until that flow exposes a stable task/timing source.
