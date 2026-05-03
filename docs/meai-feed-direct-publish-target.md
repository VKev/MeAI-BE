# MeAI Feed Direct Publish Target

## Overview

This document describes the direct publish support from Ai posts to MeAI Feed while preserving the existing publish API behavior for external social targets.

## Publish Request

`POST /api/Ai/posts/publish`

Existing request shapes remain supported:
- single object
- array of objects
- wrapper objects containing `items`, `targets`, or `posts`

New additive field per target:

```json
{
  "postId": "uuid",
  "socialMediaIds": ["uuid"],
  "isPrivate": false,
  "publishToMeAiFeed": true
}
```

Notes:
- `publishToMeAiFeed` is optional.
- Older clients can omit `publishToMeAiFeed` and continue working unchanged.
- A target may publish only to MeAI Feed, only to external social media, or to both in the same request.

## Publish Response

The response shape is unchanged except for an additive nullable field on each destination result:
- `internalTargetKey`

Example MeAI Feed destination result:

```json
{
  "socialMediaId": null,
  "socialMediaType": "meai_feed",
  "pageId": "meai_feed",
  "externalPostId": "feed-post-id",
  "publicationId": null,
  "publishStatus": "published",
  "internalTargetKey": "meai_feed"
}
```

Conventions:
- `socialMediaType = "meai_feed"`
- `pageId = "meai_feed"`
- `externalPostId` stores the created Feed post id
- `publicationId` is null because MeAI Feed direct publish is synchronous in v1

Post-level status behavior:
- Feed-only publish returns post status `published`
- Feed + external publish returns post status `processing`
- External-only publish keeps the previous async behavior

## Internal Ai -> Feed Contract

Ai calls Feed through a dedicated gRPC contract defined in `Backend/Microservices/SharedLibrary/Protos/feed_posts.proto`.

### Service

```proto
service FeedPostPublishService {
  rpc PublishAiPostToFeed (PublishAiPostToFeedRequest) returns (PublishAiPostToFeedResponse);
}
```

### Request fields

- `user_id`
- `workspace_id`
- `source_ai_post_id`
- `content`
- `resource_ids`
- `media_type`

### Response fields

- `feed_post_id`
- `created_at`

This contract is intentionally separate from `ai_feed.proto` because it represents the reverse direction: Ai initiating a Feed publish.

## Direct Publish Flow

1. Ai normalizes incoming publish targets.
2. If `publishToMeAiFeed = true`, Ai calls Feed synchronously first.
3. If Feed returns failure, the whole request fails immediately.
4. No external publish placeholder rows are created before Feed succeeds.
5. After Feed success, Ai appends an internal destination result for `meai_feed`.
6. Ai then continues the existing external publish flow for any `socialMediaIds` in the same target.

## Recursion Protection Rules

Feed normally mirrors user-created Feed posts back into Ai drafts.

To avoid recursive mirroring during Ai -> Feed direct publish:
- Feed direct publish creates the Feed post with `SkipAiMirror = true`
- Feed does not call `IAiFeedPostService.CreateMirrorPostAsync` for that path
- The created Feed post is still treated as a normal published Feed post for hashtags and notifications

This ensures:
- Ai can publish directly into Feed
- Feed does not create a mirrored Ai draft from that same direct publish
- no Ai -> Feed -> Ai loop occurs

## Duplicate Policy

Duplicate direct publish is blocked in v1.

Policy:
- one Ai post may be published to MeAI Feed only once
- Feed detects duplicates using `posts.ai_post_id`
- if a non-deleted Feed post already exists for the same `source_ai_post_id`, Feed rejects the publish
- Ai surfaces that as `Post.AlreadyPublishedToFeed`

This behavior is synchronous and deterministic.

## Feed + External Publish Behavior

When a request includes both MeAI Feed and external social targets:
- Feed publish completes first, synchronously
- external platform placeholders are created only after Feed success
- external target dispatch remains asynchronous
- if a later external platform publish fails, the already-created Feed post remains intact
- the response contains both the immediate Feed destination result and the external processing results

## Backward Compatibility Notes

Backward compatibility is preserved:
- old publish requests without `publishToMeAiFeed` still work
- existing external publish semantics are unchanged
- `internalTargetKey` is additive and nullable
- existing FE clients that ignore unknown fields remain compatible
- the existing endpoint path and accepted wrapper request formats remain unchanged

## Error Handling Summary

- empty publish targets still return the existing missing-target error
- a target with no `socialMediaIds` is now valid only when `publishToMeAiFeed = true`
- Feed duplicate publish returns `Post.AlreadyPublishedToFeed`
- Feed validation/gRPC failures abort the request before external placeholders are created
