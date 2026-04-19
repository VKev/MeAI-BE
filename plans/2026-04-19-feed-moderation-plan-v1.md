# Feed Moderation Plan

## Objective

Plan the next Feed.Microservice iteration so the team can add:

- public profile lookup by username with follower/following counts,
- public posts-by-username with tuple pagination,
- anonymous-safe post detail/comments/replies with nullable viewer-specific fields,
- admin moderation/report status workflow with admin post deletion,
- comment deletion by post owner with cascading reply deletion,
- required frontend/API documentation updates.

The current Feed service already follows the expected Clean Architecture split (`Domain`, `Application`, `Infrastructure`, `WebApi`, `test`) and is present in the solution as a dedicated microservice, so the work should extend the existing feature-first patterns rather than introduce a parallel architecture `Backend/Microservices/Microservices.sln:48-60`.

## Project Structure Summary

- Feed currently exposes all routes from a single `FeedController`, and the controller is class-level authenticated via the shared custom authorization attribute. This means every requested public read feature will require explicit route-level anonymous access decisions instead of only adding new handlers `Backend/Microservices/Feed.Microservice/src/WebApi/Controllers/FeedController.cs:24-379`, `Backend/Microservices/SharedLibrary/Attributes/AuthorizeAttribute.cs:18-58`.
- Feed stores only `UserId` references in posts, comments, and follows. It does not persist usernames or public profile metadata locally, so username-based lookup cannot be implemented as a Feed-only database query `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:11-19`, `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Comment.cs:11-17`, `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Follow.cs:11-16`.
- Feed already uses keyset pagination on `(CreatedAt, Id)` for feed posts, root comments, and replies, with shared cursor normalization and validator rules. This is the correct pattern to reuse for public posts-by-username to keep frontend paging behavior consistent `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPaginationSupport.cs:3-20`, `Backend/Microservices/Feed.Microservice/src/Application/Posts/Queries/GetFeedPostsQuery.cs:32-74`, `Backend/Microservices/Feed.Microservice/src/Application/Comments/Queries/GetCommentsByPostIdQuery.cs:28-68`, `Backend/Microservices/Feed.Microservice/src/Application/Comments/Queries/GetCommentRepliesQuery.cs:28-67`, `Backend/Microservices/Feed.Microservice/src/Application/Validators/GetFeedPostsQueryValidator.cs:6-21`.
- The current moderation flow is minimal: users can create reports and admins can list them, but there is no status transition command, no moderation audit metadata, and no admin delete action tied to the report lifecycle `Backend/Microservices/Feed.Microservice/src/Application/Reports/Commands/CreateReportCommand.cs:27-80`, `Backend/Microservices/Feed.Microservice/src/Application/Reports/Queries/GetAdminReportsQuery.cs:21-30`, `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Report.cs:13-25`.
- Comment/reply creation increments materialized counters, but there is no delete command for comments and the schema has no FK/cascade rules. Any cascading delete must therefore be implemented intentionally in application logic and validated carefully against counter integrity `Backend/Microservices/Feed.Microservice/src/Application/Comments/Commands/CreateCommentCommand.cs:63-77`, `Backend/Microservices/Feed.Microservice/src/Application/Comments/Commands/ReplyToCommentCommand.cs:72-90`, `Backend/Microservices/Feed.Microservice/src/Infrastructure/Migrations/20260417160442_InitialFeedSchema.cs:14-227`.
- The checked-in Feed API guide still documents that all Feed endpoints require login, and it does not yet cover the requested public profile/moderation/delete changes. It also overstates validation failure behavior compared with the actual middleware response, so doc updates should correct both the new feature surface and the current contract drift `docs/feed-microservice-api.md:26-32`, `docs/feed-microservice-api.md:74-93`, `Backend/Microservices/Feed.Microservice/src/WebApi/Middleware/ValidationExceptionMiddleware.cs:23-36`.

## Relevant Findings and Priority Ranking

1. **Highest priority: public user/profile and public media dependency design**  
   Feed cannot resolve `username -> userId` on its own, and the only current gRPC contract with User.Microservice is owner-scoped resource presigning. That is the main architectural blocker for public profile lookup and public posts-by-username `Backend/Microservices/SharedLibrary/Protos/user_resources.proto:7-93`, `Backend/Microservices/Feed.Microservice/src/Application/Abstractions/Resources/IUserResourceService.cs:5-17`, `Backend/Microservices/User.Microservice/src/Application/Resources/Queries/GetResourcesByIdsQuery.cs:40-50`.
2. **Second priority: cascade-delete correctness**  
   Comment deletion affects `Post.CommentsCount`, `Comment.RepliesCount`, reply visibility, and possibly report relevance. Because the schema does not enforce cascades, incorrect implementation can silently corrupt counters `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:21-36`, `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Comment.cs:19-32`, `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/CommentConfiguration.cs:14-29`.
3. **Third priority: moderation workflow auditability**  
   The existing `Report` model only stores `Status`, `CreatedAt`, and `UpdatedAt`, which is too thin for a real admin workflow where status changes and post deletions must be explainable later `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Report.cs:17-25`, `Backend/Microservices/Feed.Microservice/src/Application/Reports/Models/ReportModels.cs:5-27`.
4. **Fourth priority: anonymous-safe response semantics**  
   Current GET routes expect an authenticated viewer and `PostResponse.IsLikedByCurrentUser` is non-nullable. Public reads need a deliberate nullable viewer contract instead of silently returning `false` for anonymous requests `Backend/Microservices/Feed.Microservice/src/WebApi/Controllers/FeedController.cs:83-235`, `Backend/Microservices/Feed.Microservice/src/Application/Posts/Models/PostModels.cs:12-25`.

## Affected Files and Modules

### Feed.Microservice

- **Routing and OpenAPI surface**
  - `Backend/Microservices/Feed.Microservice/src/WebApi/Controllers/FeedController.cs:24-394`
  - `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:15-18`
  - `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:59-65`
  - `Backend/Microservices/SharedLibrary/Attributes/AuthorizeAttribute.cs:18-58`
  - Implication: route-level `AllowAnonymous` usage, new admin/public endpoints, and response annotations must be updated together.

- **Post read models and public-read behavior**
  - `Backend/Microservices/Feed.Microservice/src/Application/Posts/Models/PostModels.cs:12-60`
  - `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPostSupport.cs:97-212`
  - `Backend/Microservices/Feed.Microservice/src/Application/Posts/Queries/GetPostByIdQuery.cs:12-46`
  - `Backend/Microservices/Feed.Microservice/src/Application/Posts/Queries/GetFeedPostsQuery.cs:11-75`
  - Implication: viewer-specific fields, author-profile enrichment strategy, and public media resolution must be updated in one place.

- **Comment read/delete behavior**
  - `Backend/Microservices/Feed.Microservice/src/Application/Comments/Models/CommentModels.cs:5-31`
  - `Backend/Microservices/Feed.Microservice/src/Application/Comments/Queries/GetCommentsByPostIdQuery.cs:11-69`
  - `Backend/Microservices/Feed.Microservice/src/Application/Comments/Queries/GetCommentRepliesQuery.cs:11-68`
  - `Backend/Microservices/Feed.Microservice/src/Application/Comments/Commands/CreateCommentCommand.cs:13-79`
  - `Backend/Microservices/Feed.Microservice/src/Application/Comments/Commands/ReplyToCommentCommand.cs:13-92`
  - Implication: comment DTOs need viewer/delete flags, and delete logic must reverse counter increments across the full subtree.

- **Moderation/report workflow**
  - `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Report.cs:6-26`
  - `Backend/Microservices/Feed.Microservice/src/Application/Reports/Commands/CreateReportCommand.cs:12-82`
  - `Backend/Microservices/Feed.Microservice/src/Application/Reports/Queries/GetAdminReportsQuery.cs:10-32`
  - `Backend/Microservices/Feed.Microservice/src/Application/Reports/Models/ReportModels.cs:5-29`
  - `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/ReportConfiguration.cs:7-25`
  - Implication: moderation state transitions, admin action metadata, and admin-facing query filtering will expand this feature area materially.

- **Delete semantics and soft-delete rules**
  - `Backend/Microservices/Feed.Microservice/src/Application/Posts/Commands/DeletePostCommand.cs:11-64`
  - `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:6-37`
  - `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Comment.cs:6-33`
  - Implication: admin post deletion can reuse soft-delete semantics, but report-side audit data must tell owner deletion and moderator deletion apart.

- **Pagination and index strategy**
  - `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPaginationSupport.cs:3-20`
  - `Backend/Microservices/Feed.Microservice/src/Application/Validators/GetFeedPostsQueryValidator.cs:6-21`
  - `Backend/Microservices/Feed.Microservice/src/Application/Validators/GetCommentsByPostIdQueryValidator.cs:6-21`
  - `Backend/Microservices/Feed.Microservice/src/Application/Validators/GetCommentRepliesQueryValidator.cs:6-21`
  - `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/PostConfiguration.cs:14-29`
  - `Backend/Microservices/Feed.Microservice/src/Infrastructure/Migrations/20260418073745_AddFeedPaginationIndexes.cs:11-42`
  - Implication: posts-by-username should reuse the same cursor contract but likely needs a stronger composite index by author.

### Cross-service dependencies outside Feed

- **User lookup and profile data source**
  - `Backend/Microservices/User.Microservice/src/Domain/Entities/User.cs:6-45`
  - `Backend/Microservices/User.Microservice/src/Infrastructure/Context/Configuration/UserConfiguration.cs:15-24`
  - `Backend/Microservices/User.Microservice/src/Application/Users/Models/UserProfileResponse.cs:3-18`
  - `Backend/Microservices/User.Microservice/src/WebApi/Controllers/AuthController.cs:110-128`
  - Implication: username uniqueness already exists, but the only checked-in profile route is authenticated `/me`; Feed needs a new public user contract rather than reusing the private profile model verbatim.

- **Current gRPC/resource integration**
  - `Backend/Microservices/SharedLibrary/Protos/user_resources.proto:7-93`
  - `Backend/Microservices/Feed.Microservice/src/Infrastructure/DependencyInjection.cs:26-43`
  - `Backend/Microservices/Feed.Microservice/src/Infrastructure/Logic/Resources/UserResourceGrpcService.cs:17-50`
  - `Backend/Microservices/User.Microservice/src/Application/Resources/Queries/GetResourcesByIdsQuery.cs:40-50`
  - Implication: current Feed->User integration is owner-scoped resource presigning only; public post media/avatar access needs either a new public resource RPC or a broader public profile/media contract.

### Documentation

- `docs/feed-microservice-api.md:26-32`
- `docs/feed-microservice-api.md:74-93`
- `docs/feed-microservice-api.md:97-148`
- `docs/feed-microservice-api.md:178-224`
- Implication: the frontend guide must be updated for new public routes, nullable viewer fields, moderation flows, and the corrected validation-response note.

## Assumptions and Scope Decisions

- This plan assumes username resolution remains owned by User.Microservice instead of denormalizing usernames into Feed tables, because Feed currently stores only user GUIDs and the User service already enforces active-username uniqueness `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:11-19`, `Backend/Microservices/User.Microservice/src/Infrastructure/Context/Configuration/UserConfiguration.cs:15-17`.
- This plan assumes public read routes continue to return the existing `Result<T>` success envelope and `ProblemDetails`-style business failures, while the documentation is corrected to note the current validation middleware behavior for invalid requests `Backend/Microservices/SharedLibrary/Common/ApiController.cs:20-48`, `Backend/Microservices/Feed.Microservice/src/WebApi/Middleware/ValidationExceptionMiddleware.cs:23-36`.
- This plan assumes the first deletion scope is **post-owner comment deletion with reply cascade**. Extending the same endpoint to comment authors or moderators can be evaluated later, but is intentionally not required for the initial change set.
- This plan assumes anonymous viewers should receive `null` for viewer-specific booleans instead of synthetic `false`, so the frontend can distinguish “not authenticated” from “authenticated but not allowed/not liked.”

## Proposed API Contracts

### 1) Public profile by username

**Recommended route**: `GET /api/Feed/profiles/{username}`  
**Auth**: `AllowAnonymous`  
**Success contract**: `Result<PublicProfileResponse>`

```json
{
  "isSuccess": true,
  "isFailure": false,
  "value": {
    "userId": "guid",
    "username": "string",
    "fullName": "string | null",
    "avatarUrl": "string | null",
    "followerCount": 0,
    "followingCount": 0
  }
}
```

**Why this contract**

- Feed already owns follower/following rows, so counts should be computed in Feed from `Follow` records instead of being duplicated in User.Microservice `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Follow.cs:6-16`, `Backend/Microservices/Feed.Microservice/src/Application/Follows/Models/FollowModels.cs:3-5`.
- Username/profile metadata must come from User.Microservice because Feed does not store it locally `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:11-19`, `Backend/Microservices/User.Microservice/src/Domain/Entities/User.cs:11-30`.

### 2) Public posts by username with tuple pagination

**Recommended route**: `GET /api/Feed/profiles/{username}/posts?cursorCreatedAt={iso}&cursorId={guid}&limit={n}`  
**Auth**: `AllowAnonymous`  
**Success contract**: `Result<IReadOnlyList<PostResponse>>`

**Contract notes**

- Reuse the existing `(cursorCreatedAt, cursorId, limit)` cursor semantics so frontend infinite scroll does not need a second paging model `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPaginationSupport.cs:3-20`, `docs/feed-microservice-api.md:447-478`.
- Resolve `username -> userId` first, then apply the same ordered `(CreatedAt desc, Id desc)` keyset filter currently used by the feed query `Backend/Microservices/Feed.Microservice/src/Application/Posts/Queries/GetFeedPostsQuery.cs:47-74`.

### 3) Public post detail/comments/replies with nullable viewer-specific fields

**Recommended routes**

- `GET /api/Feed/posts/{id}`
- `GET /api/Feed/posts/{id}/comments?cursorCreatedAt={iso}&cursorId={guid}&limit={n}`
- `GET /api/Feed/comments/{id}/replies?cursorCreatedAt={iso}&cursorId={guid}&limit={n}`

**Auth**: `AllowAnonymous`, with optional viewer resolution if a token is present.

**Recommended DTO adjustments**

- `PostResponse`
  - keep existing fields,
  - change `isLikedByCurrentUser` to `bool?`,
  - add `canDelete` as `bool?`.
- `CommentResponse`
  - keep existing fields,
  - add `canDelete` as `bool?`.

**Nullable viewer semantics**

- authenticated viewer: `true` / `false`
- anonymous viewer: `null`

This is necessary because `PostResponse.IsLikedByCurrentUser` is currently non-nullable and all three GET routes currently reject anonymous users before reaching the handlers `Backend/Microservices/Feed.Microservice/src/Application/Posts/Models/PostModels.cs:12-25`, `Backend/Microservices/Feed.Microservice/src/WebApi/Controllers/FeedController.cs:83-235`.

### 4) Admin moderation/report workflow

**Recommended route**: `PATCH /api/Feed/admin/reports/{id}`  
**Auth**: admin only  
**Request**

```json
{
  "status": "InReview | Resolved | Dismissed",
  "action": "None | DeleteTargetPost",
  "resolutionNote": "string | null"
}
```

**Recommended response**: `Result<ReportResponse>` with these additional fields:

- `reviewedByAdminId: guid | null`
- `reviewedAt: string | null`
- `resolutionNote: string | null`
- `actionType: string | null`

**Behavior rules**

- `Pending` remains the default at report creation.
- `DeleteTargetPost` is valid only when `targetType == "Post"`.
- When `DeleteTargetPost` succeeds, the report should end in `Resolved` and record the admin + timestamp in the same transaction.

### 5) Comment deletion by post owner

**Recommended route**: `DELETE /api/Feed/comments/{id}`  
**Auth**: authenticated  
**Success contract**: `Result<bool>` for consistency with current delete-post behavior, unless the frontend explicitly needs a richer optimistic-update payload.

**Authorization rule**

- allow when the requesting user owns the parent post.
- reject when the comment or parent post is already deleted/not found.

**Delete behavior**

- soft-delete the selected comment,
- soft-delete all descendant replies,
- decrement `Post.CommentsCount` by the total number of newly deleted nodes,
- decrement the direct parent’s `RepliesCount` when deleting a reply subtree.

### 6) Admin report listing

**Recommended route**: `GET /api/Feed/admin/reports?status={status?}&targetType={type?}`  
**Auth**: admin only  
**Success contract**: `Result<IReadOnlyList<ReportResponse>>`

Keep the current route but add optional filtering to support the new status-based workflow instead of always returning one undifferentiated list `Backend/Microservices/Feed.Microservice/src/Application/Reports/Queries/GetAdminReportsQuery.cs:21-30`.

## Data Model Changes

### Recommended Feed schema changes

- **`reports` table**
  - add `reviewed_by_admin_id uuid null`
  - add `reviewed_at timestamptz null`
  - add `resolution_note text null`
  - add `action_type text null`
  - keep `status` as a constrained application enum/string with allowed values `Pending`, `InReview`, `Resolved`, `Dismissed`

Rationale: the current `Report` entity cannot explain who changed a status or whether a post was deleted as part of moderation `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Report.cs:13-25`.

- **`posts` table**
  - no mandatory column addition is required for the requested scope if admin deletion continues using the existing soft-delete fields,
  - add a composite index on `(user_id, created_at, id)` for public posts-by-username.

Rationale: current indexes support `user_id` lookups and global `(created_at, id)` ordering separately, but a username-profile timeline query benefits from an author-first keyset index `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/PostConfiguration.cs:14-29`.

- **`comments` table**
  - no mandatory schema change is required for basic soft-delete cascade because `IsDeleted`/`DeletedAt` already exist,
  - do **not** rely on database FK cascade for the first iteration because the current schema is not FK-driven.

Rationale: application-managed cascade is lower-risk than retrofitting self-referencing FK cascade into a live comment tree with soft-delete semantics `Backend/Microservices/Feed.Microservice/src/Infrastructure/Migrations/20260417160442_InitialFeedSchema.cs:28-47`, `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/CommentConfiguration.cs:11-29`.

### Cross-service contract changes

- Add a new User.Microservice public-profile lookup contract keyed by username.
- Add a public-safe media/avatar lookup contract for Feed read scenarios, because the existing resource gRPC path only returns resources owned by the requesting user `Backend/Microservices/SharedLibrary/Protos/user_resources.proto:21-35`, `Backend/Microservices/User.Microservice/src/Application/Resources/Queries/GetResourcesByIdsQuery.cs:40-50`.

## Migration Considerations

- Add a new Feed EF migration for report-review columns and the author-timeline composite index.
- Ensure the migration is backward-compatible for existing rows by keeping all new moderation columns nullable and leaving `status` populated as `Pending`/existing values during rollout.
- Do not attempt to retrofit hard-delete cascades or mandatory FKs into the initial moderation release; use application-level soft-delete cascade first to avoid data-loss risk.
- Coordinate any shared gRPC/proto changes across Feed and User services in the same implementation window so generated clients remain compatible `Backend/Microservices/Feed.Microservice/src/Infrastructure/DependencyInjection.cs:26-43`, `Backend/Microservices/SharedLibrary/Protos/user_resources.proto:7-93`.
- Re-check production/dev compose and gateway routing once the new Feed endpoints are in place so `/api/Feed/...` remains exposed through the gateway and public docs stay accurate `Backend/Compose/docker-compose.yml:145-210`, `docs/feed-microservice-api.md:15-23`.

## Implementation Plan

- [ ] **Task 1. Define the public user/profile dependency boundary.** Create a dedicated public-user abstraction for Feed (preferred: new public profile/media contract to User.Microservice) instead of overloading the current owner-scoped resource service. This is necessary because Feed stores only user GUIDs while the current gRPC contract only supports owner-specific resource presigning `Backend/Microservices/Feed.Microservice/src/Application/Abstractions/Resources/IUserResourceService.cs:5-17`, `Backend/Microservices/User.Microservice/src/Application/Resources/Queries/GetResourcesByIdsQuery.cs:40-50`.
- [ ] **Task 2. Add a public profile read feature keyed by username.** Introduce a new Application query/model pair for `GET /profiles/{username}` that resolves username/user metadata from User.Microservice and derives follower/following counts from Feed’s `Follow` table. This is necessary because Feed already owns the social graph but not the public identity record `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Follow.cs:6-16`, `Backend/Microservices/User.Microservice/src/Infrastructure/Context/Configuration/UserConfiguration.cs:15-17`.
- [ ] **Task 3. Add a posts-by-username public timeline query using the existing tuple-pagination contract.** Reuse the same `(cursorCreatedAt, cursorId, limit)` normalization, validation, ordering, and page-size rules already used for authenticated feed reads. This keeps frontend infinite-scroll behavior consistent and minimizes new paging logic `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPaginationSupport.cs:3-20`, `Backend/Microservices/Feed.Microservice/src/Application/Posts/Queries/GetFeedPostsQuery.cs:32-74`.
- [ ] **Task 4. Make post detail, root comments, and replies anonymous-safe.** Mark the three read routes as anonymous-capable, pass an optional viewer id into the query layer when present, and return nullable viewer-specific fields (`isLikedByCurrentUser`, `canDelete`) instead of returning `401`. This is necessary because all current reads are blocked by controller-level auth and `PostResponse` still assumes a logged-in viewer `Backend/Microservices/Feed.Microservice/src/WebApi/Controllers/FeedController.cs:83-235`, `Backend/Microservices/Feed.Microservice/src/Application/Posts/Models/PostModels.cs:12-25`.
- [ ] **Task 5. Expand Feed response models for public/profile/moderation use cases.** Add new public profile models and extend existing post/comment/report models with only additive fields required by the frontend. This is necessary to preserve the established `Result<T>` envelope while surfacing moderation metadata and anonymous-safe viewer context `Backend/Microservices/Feed.Microservice/src/Application/Posts/Models/PostModels.cs:12-60`, `Backend/Microservices/Feed.Microservice/src/Application/Comments/Models/CommentModels.cs:5-31`, `Backend/Microservices/Feed.Microservice/src/Application/Reports/Models/ReportModels.cs:5-29`.
- [ ] **Task 6. Implement comment deletion for post owners with subtree-aware soft-delete cascade.** Add a delete command/route that validates the parent post owner, finds the selected comment and all descendants, marks them deleted, and repairs `Post.CommentsCount` plus any direct-parent `RepliesCount`. This is necessary because the current service only increments counters on create and has no database cascade support to repair them automatically `Backend/Microservices/Feed.Microservice/src/Application/Comments/Commands/CreateCommentCommand.cs:63-77`, `Backend/Microservices/Feed.Microservice/src/Application/Comments/Commands/ReplyToCommentCommand.cs:72-90`, `Backend/Microservices/Feed.Microservice/src/Infrastructure/Migrations/20260417160442_InitialFeedSchema.cs:14-227`.
- [ ] **Task 7. Extend the report domain/application model into an actual moderation workflow.** Add status transition handling, admin audit fields, and a moderation action model that can resolve a report and soft-delete a target post in the same transaction. This is necessary because the current implementation creates `Pending` reports and lists them, but does not support review, dismissal, or action tracking `Backend/Microservices/Feed.Microservice/src/Application/Reports/Commands/CreateReportCommand.cs:27-80`, `Backend/Microservices/Feed.Microservice/src/Application/Reports/Queries/GetAdminReportsQuery.cs:21-30`, `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Report.cs:13-25`.
- [ ] **Task 8. Add the supporting EF configuration and migration changes.** Update `ReportConfiguration`, add the report-review columns, and add a composite author-timeline index for public profile posts. This is necessary for query performance and moderation audit persistence `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/ReportConfiguration.cs:11-24`, `Backend/Microservices/Feed.Microservice/src/Infrastructure/Context/Configuration/PostConfiguration.cs:14-29`.
- [ ] **Task 9. Update API documentation, controller annotations, and frontend integration notes together.** Refresh the checked-in Feed guide and OpenAPI-facing route annotations so the repository docs describe which routes are public, which viewer fields can be `null`, how tuple pagination works for public timelines, and how admin/report actions behave. This is necessary because the current guide says all Feed endpoints require login and does not describe the new workflows `docs/feed-microservice-api.md:26-32`, `docs/feed-microservice-api.md:74-93`, `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:59-65`.
- [ ] **Task 10. Add verification coverage at the application and API boundary.** Extend Feed tests beyond architecture rules with focused handler/controller tests for anonymous reads, tuple pagination, moderation status transitions, and cascade delete counter repair. This is necessary because the checked-in Feed test project currently verifies architecture boundaries only `Backend/Microservices/Feed.Microservice/test/ArchitectureTest.cs:13-92`.

## Verification Criteria

- [ ] Anonymous requests to `GET /api/Feed/posts/{id}`, `GET /api/Feed/posts/{id}/comments`, and `GET /api/Feed/comments/{id}/replies` succeed for non-deleted content and return `null` for viewer-specific fields rather than `401`.
- [ ] `GET /api/Feed/profiles/{username}` returns the resolved public profile plus correct follower/following counts for an existing, non-deleted username.
- [ ] `GET /api/Feed/profiles/{username}/posts` uses the same cursor pair rules as the existing feed endpoints and returns pages ordered by `(createdAt desc, id desc)`.
- [ ] Post-owner comment deletion soft-deletes the selected comment plus all descendants, decrements `Post.CommentsCount` accurately, and leaves no orphaned visible replies.
- [ ] Admin moderation can move a report through the planned statuses, record reviewer metadata, and soft-delete a reported post when the chosen action requires it.
- [ ] The checked-in Feed API guide and generated OpenAPI surface both describe the new public/admin routes, nullable viewer fields, and moderation response shapes.

## Potential Risks and Mitigations

1. **Public media/avatar access remains owner-scoped.**  
   Mitigation: treat public profile/media access as a first-class contract change with User.Microservice instead of trying to reuse the current owner-scoped resource lookup path.
2. **Comment subtree delete corrupts counters or leaves visible descendants.**  
   Mitigation: delete descendants in a deterministic order, count only newly affected nodes, and verify counter repair with multi-level comment-tree tests.
3. **Report status values drift because they remain free-form strings.**  
   Mitigation: centralize allowed statuses/actions in application constants or enum-backed mapping and validate every transition before persistence.
4. **Anonymous-safe route changes accidentally weaken write-side authorization.**  
   Mitigation: scope `AllowAnonymous` only to the three public GET routes and keep create/delete/follow/report endpoints under the existing auth checks.
5. **Documentation and frontend assumptions drift from runtime behavior again.**  
   Mitigation: update the checked-in guide and controller response annotations in the same implementation batch, explicitly documenting the current validation-error shape as well as the new public/admin flows.

## Alternative Approaches

1. **Recommended approach: User-service lookup + Feed-owned counts + application-managed soft-delete cascade.** This minimizes Feed schema growth, reuses the existing tuple-pagination pattern, and fits current soft-delete semantics, but it requires a coordinated shared-contract update with User.Microservice.
2. **Alternative approach: denormalize public user metadata into Feed read models.** This would make username/profile queries faster and reduce cross-service reads, but it introduces sync complexity, stale profile risk, and new event-driven consistency work that does not exist in the current Feed design.
3. **Alternative approach: database-enforced FK cascades for comments/replies.** This could simplify hard-delete trees, but it conflicts with the current soft-delete model and requires higher-risk schema redesign because the current Feed schema is not FK-driven.
4. **Alternative approach: separate manual admin post-delete endpoint outside the report workflow.** This is simpler for emergency moderation, but it weakens auditability compared with a report-centric moderation action that resolves status and deletes content atomically.
