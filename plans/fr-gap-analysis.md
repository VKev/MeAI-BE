# FR Gap Analysis

Source baseline:
- [FR.txt](../FR.txt)
- current diagram tracker: [frontend-feature-diagram-tracker.md](./frontend-feature-diagram-tracker.md)

Scope of this review:
- compare `FR.txt` against the current implemented surface in `MeAI-BE`, `MeAI-FE`, and `MeAI-Social-Platform`
- distinguish between:
  - `covered`: implemented and already represented by the current feature diagrams
  - `partial / backend-only`: implementation exists, but current FE flow or current diagrams do not fully expose it
  - `missing / unclear`: no convincing end-to-end implementation was found

## Covered

These FR groups are materially covered by the current 24 feature diagrams and by code:
- `FR-U1` auth/profile
- `FR-U2.1`, `FR-U2.2`, `FR-U2.4` workspace creation/rename/delete and AI flow in workspace
- `FR-U3.1`, `FR-U3.3` AI draft image/video generation and editable draft flow
- `FR-U4.1`, `FR-U4.2` uploaded resources used in generation
- `FR-U5.2` edit post metadata
- `FR-U6.1`, `FR-U6.2` view content and manage state
- `FR-U7.1`, `FR-U7.2`, `FR-U7.3` analytics and AI recommendation
- `FR-U8.1`, `FR-U8.2`, `FR-U8.4` schedule creation, attach posts, auto publish
- `FR-U9.1`, `FR-U9.2`, `FR-U9.3` social account connect and platform-aware publishing
- `FR-U10.1`, `FR-U10.2`, `FR-U10.3`, `FR-U10.5`, `FR-U10.6` publish to MeAI feed, comment/reply, follow/unfollow, hashtags, reports
- `FR-U10.4` realtime notifications for followed-feed activity appears covered by Notification service + SignalR
- `FR-U11.1`, `FR-U11.2`, `FR-U11.3` subscription packages, purchase/upgrade, VIP marker
- `FR-U12.1`, `FR-U12.2` free tier and quota enforcement
- `FR-U13.1`, `FR-U13.2`, `FR-U13.3` Stripe payments and transaction linkage
- `FR-A1` admin user management
- `FR-A2` admin report management
- `FR-A3` admin subscription management
- `FR-A4` admin transaction management
- `FR-A5` admin API key management
- `FR-A6` admin system spending overview/trends
- `FR-A7` admin storage/resource management including cleanup policy
- `FR-A8.1` aggregated revenue statistics

Representative evidence:
- schedule UI in FE: [DialogPublishPost.tsx](../../MeAI-FE/app/components/preview/common/DialogPublishPost.tsx)
- schedule API in BE: [PostsController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs), [SchedulesController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/SchedulesController.cs)
- MeAI feed + publish flag: [post.client.ts](../../MeAI-FE/app/services/client/post.client.ts), [PostsController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs)
- comments/follows/reports: [FeedController.cs](../Backend/Microservices/Feed.Microservice/src/WebApi/Controllers/FeedController.cs)
- realtime notifications: [use-notifications.tsx](../../MeAI-Social-Platform/src/hooks/use-notifications.tsx), [NotificationHub.cs](../Backend/Microservices/Notification.Microservice/src/WebApi/Hubs/NotificationHub.cs)
- VIP badge: [UserAvatar.tsx](../../MeAI-FE/app/components/common/UserAvatar.tsx)
- admin revenue widget: [dashboard.tsx](../../MeAI-FE/app/routes/admin/dashboard.tsx)
- admin storage cleanup/settings: [admin.client.ts](../../MeAI-FE/app/services/client/admin.client.ts), [AdminStorageController.cs](../Backend/Microservices/User.Microservice/src/WebApi/Controllers/AdminStorageController.cs)

## Partial Or Backend-Only

These FRs are not absent, but the current feature-diagram set does not show them as first-class flows, or the FE exposure looks incomplete.

| FR | Status | Evidence |
|---|---|---|
| `FR-U3.2` internet retrieval for AI | backend present | AI service wires Brave search and web-search enrichment: [DependencyInjection.cs](../Backend/Microservices/Ai.Microservice/src/Infrastructure/DependencyInjection.cs), [WebSearchEnrichmentService.cs](../Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Automation/WebSearchEnrichmentService.cs) |
| `FR-U3.4` enhance/refine existing draft | backend present, not surfaced in current FE feature set | enhance endpoint exists: [PostsController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs), [EnhanceExistingPostCommand.cs](../Backend/Microservices/Ai.Microservice/src/Application/Posts/Commands/EnhanceExistingPostCommand.cs) |
| `FR-U3.7` suggest posts similar to already published MeAI content | partial | recommendation and recommend-post pipelines exist: [RecommendationsController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/RecommendationsController.cs), [RecommendPostGenerationConsumer.cs](../Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Consumers/RecommendPostGenerationConsumer.cs), but the current diagrams focus on generic recommendation rather than explicit "similar to published MeAI post" flow |
| `FR-U8.3` AI assembles suitable content for a schedule slot | partial | schedule runtime generation exists for agentic schedules in backend: [HandleAgentScheduleRuntimeResultCommand.cs](../Backend/Microservices/Ai.Microservice/src/Application/PublishingSchedules/Commands/HandleAgentScheduleRuntimeResultCommand.cs), [InternalAgentSchedulesController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/InternalAgentSchedulesController.cs); however `SchedulesController` rejects agentic mode for current public FE path: [SchedulesController.cs](../Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/SchedulesController.cs) |
| `FR-U2.3` group posts by content/topic within workspace | partial / weak | workspace has a `type` taxonomy in FE: [workspace.tsx](../../MeAI-FE/app/routes/user/workspace.tsx), but the content list is currently organized mainly by status/platform/account filters in [product.tsx](../../MeAI-FE/app/routes/user/product.tsx), not clearly by topic/content buckets |
| `FR-U6.3` organize content by tags/folders/filters | partial | filters by platform/account/status are present in [product.tsx](../../MeAI-FE/app/routes/user/product.tsx); true user-defined folders/tags were not found |
| `FR-A8.2` revenue breakdown by subscription package or user segment | partial | aggregated revenue is visible in [dashboard.tsx](../../MeAI-FE/app/routes/admin/dashboard.tsx), but I did not find a clear FE/API surface for package/segment breakdown |

## Missing Or Unclear

These are the main FRs that currently look genuinely missing, or at least lack convincing end-to-end implementation.

| FR | Assessment | Why |
|---|---|---|
| `FR-U3.5` AI proposes background music for generated videos | likely missing | only a workspace type label `music` was found in [workspace.tsx](../../MeAI-FE/app/routes/user/workspace.tsx); no dedicated API/flow for music suggestion was found |
| `FR-U3.6` Nano Banana-style automatic clipping/cutting into short social-ready videos | likely missing | no convincing user-facing FE or backend clipping flow was found; the only strong video feature found is extension/generation, not automatic clipping |
| `FR-U3.8` long-form video generation around 30 minutes | likely missing / unverified | video generation and extension exist, but no explicit long-form 30-minute flow or contract was found |
| `FR-U5.1` edit generated/uploaded videos by cut/trim/replace media | weak / likely missing as full feature | current FE and BE clearly support post metadata updates and media selection, but I did not find a concrete end-to-end trim/cut video editor flow |
| `FR-U10.7` chat and video calls between users | missing | no convincing messaging, conversation, WebRTC, or call feature surface was found in FE or backend |
| `FR-U10.8` save/bookmark posts for later viewing | missing | no bookmark/save-post API or FE flow was found; search hits were only seed/demo/knowledge noise, not product functionality |

## Bottom Line

If the question is "what is the system still missing compared with `FR.txt`?", the highest-confidence missing items are:
- `FR-U3.5` background music suggestion
- `FR-U3.6` automatic clipping/cutting to short social-ready videos
- `FR-U3.8` long-form ~30-minute video generation
- `FR-U5.1` true video editing flow with cut/trim/replace
- `FR-U10.7` user chat/video calls
- `FR-U10.8` bookmark/save post

And the main partial items are:
- `FR-U2.3` topic/content grouping in workspace
- `FR-U3.2` web retrieval is implemented in backend but not obvious in the FE diagrams
- `FR-U3.4` enhance existing draft exists in backend but is not part of the current FE feature-diagram set
- `FR-U3.7` "similar to published MeAI content" is only partially evidenced through the recommendation pipeline
- `FR-U8.3` agentic schedule-slot content generation exists in backend but is not clearly exposed in the public FE flow
- `FR-U6.3` filters exist, but user-defined tags/folders are not clearly implemented
- `FR-A8.2` revenue breakdown by package/segment is still weak
