# Feed AI Dashboard Analytics Integration

## Mục tiêu

Tài liệu này mô tả cách backend hiện đang triển khai dashboard analytics cho `Feed.Microservice` thông qua `Ai.Microservice`, đồng thời hướng dẫn cách tích hợp trong một ứng dụng **Vite + React + TypeScript**.

Mục tiêu của thiết kế này là:

- giữ `Feed.Microservice` làm **source of truth** cho dữ liệu nội bộ của feed
- giữ `Ai.Microservice` làm lớp **normalization + public analytics API** giống các dashboard social khác
- giúp frontend có thể tái sử dụng tối đa components/hook đã dùng cho Facebook / Instagram / TikTok / Threads dashboards
- không bịa thêm các metric mà Feed chưa track như `views`, `reach`, `impressions`, `shares`, `reposts`, `quotes`, `saves`

---

## 1. Backend flow tổng thể

Luồng backend hiện tại chạy theo 4 lớp:

1. **Feed counters và entities**
   - Feed lưu `LikesCount`, `CommentsCount`, `CreatedAt`, `UpdatedAt` trên post và `RepliesCount` trên comment tại `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:6-35` và `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Comment.cs:6-33`.
2. **Feed analytics aggregation layer**
   - Feed tổng hợp dashboard summary và per-post analytics tại `Backend/Microservices/Feed.Microservice/src/Application/Analytics/Queries/GetFeedDashboardSummaryQuery.cs:12-219` và `Backend/Microservices/Feed.Microservice/src/Application/Analytics/Queries/GetFeedPostAnalyticsQuery.cs:12-237`.
3. **Internal gRPC contract giữa AI và Feed**
   - Shared proto mới định nghĩa contract nội bộ tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:1-83`.
   - Feed expose gRPC service tại `Backend/Microservices/Feed.Microservice/src/WebApi/Grpc/FeedAnalyticsGrpcService.cs:8-156`.
4. **AI normalization + public API exposure**
   - AI gọi Feed qua gRPC client tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:10-259`.
   - AI expose public endpoints tại `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs:199-249`.

---

## 2. Dữ liệu Feed được lấy từ đâu

### 2.1 Post-level source data

Feed analytics dựa trên các field sẵn có trong domain:

- `LikesCount` của post tại `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:21`
- `CommentsCount` của post tại `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:23`
- `CreatedAt`, `UpdatedAt` của post tại `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Post.cs:25-29`
- `ParentCommentId`, `LikesCount`, `RepliesCount` của comment tại `Backend/Microservices/Feed.Microservice/src/Domain/Entities/Comment.cs:15-21`
- hashtag mapping qua `PostHashtag` tại `Backend/Microservices/Feed.Microservice/src/Domain/Entities/PostHashtag.cs:6-16`

### 2.2 Derived metrics được Feed tự tính

Feed analytics không chỉ đọc raw counters mà còn aggregate thêm:

- `TopLevelComments`
- `Replies`
- `TotalDiscussion`
- `TotalInteractions`
- `MediaCount`
- `HashtagCount`

Model nội bộ cho analytics được định nghĩa tại `Backend/Microservices/Feed.Microservice/src/Application/Analytics/Models/FeedAnalyticsModels.cs:3-56`.

### 2.3 Những metric chưa có ở Feed

Feed phase hiện tại **không track native** các giá trị sau:

- `Views`
- `Reach`
- `Impressions`
- `Shares`
- `Reposts`
- `Quotes`
- `Saves`

Vì vậy AI layer sẽ giữ các field này là `null` thay vì suy diễn số liệu, theo mapping tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:147-163`.

---

## 3. Feed analytics aggregation hoạt động như thế nào

### 3.1 Dashboard summary cho username

Feed query `GetFeedDashboardSummaryQueryHandler`:

- resolve username sang public profile qua `IUserResourceService`
- lấy recent posts theo thứ tự mới nhất
- giới hạn số lượng post theo `postLimit`
- tính `HasMorePosts`
- batch load hashtags
- batch load public presigned media
- batch aggregate comments thành `TopLevelComments`, `Replies`, `TotalDiscussion`
- tính aggregated summary cho toàn dashboard

Implementation nằm tại `Backend/Microservices/Feed.Microservice/src/Application/Analytics/Queries/GetFeedDashboardSummaryQuery.cs:17-219`.

### 3.2 Per-post analytics

Feed query `GetFeedPostAnalyticsQueryHandler`:

- tìm post đang active
- resolve author profile
- load hashtags + media cho riêng post đó
- aggregate discussion breakdown
- lấy top-level comment samples theo `commentSampleLimit`
- trả về profile + post + comment samples

Implementation nằm tại `Backend/Microservices/Feed.Microservice/src/Application/Analytics/Queries/GetFeedPostAnalyticsQuery.cs:17-237`.

### 3.3 Reuse support logic trong Feed

Feed tận dụng các helper có sẵn để tránh duplicate logic:

- load hashtags theo post ids tại `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPostSupport.cs:69-97`
- load public presigned resources tại `Backend/Microservices/Feed.Microservice/src/Application/Common/FeedPostSupport.cs:99-134`

Điều này giúp analytics layer giữ đúng source-of-truth và không phải viết lại logic resolve media/hashtags.

---

## 4. Internal gRPC contract giữa AI và Feed

Proto mới nằm tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:1-83`.

### 4.1 RPC methods

- `GetDashboardSummary` tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:7-9`
- `GetPostAnalytics` tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:7-9`

### 4.2 Request shapes

- `GetFeedDashboardSummaryRequest` có:
  - `requester_user_id`
  - `username`
  - `post_limit`
  tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:57-61`
- `GetFeedPostAnalyticsRequest` có:
  - `requester_user_id`
  - `post_id`
  - `comment_sample_limit`
  tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:73-77`

### 4.3 Response shapes

- dashboard summary response tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:63-71`
- per-post analytics response tại `Backend/Microservices/SharedLibrary/Protos/feed_analytics.proto:79-83`

### 4.4 Feed server wiring

Feed WebApi bật gRPC server và map `FeedAnalyticsGrpcService` tại:

- gRPC listen port `5008` ở `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:18-28`
- `AddGrpc()` ở `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:30-34`
- `MapGrpcService<FeedAnalyticsGrpcService>()` ở `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:84`
- proto compile server-side tại `Backend/Microservices/Feed.Microservice/src/WebApi/WebApi.csproj:36-38`

---

## 5. AI microservice normalize Feed data như thế nào

### 5.1 Client registration

AI đăng ký gRPC client tới Feed analytics service tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/DependencyInjection.cs:96-104`.

Mặc định AI gọi tới:

- `FeedService__GrpcUrl`
- fallback `http://feed-microservice:5008`

### 5.2 Client wrapper

`FeedAnalyticsGrpcClient` triển khai `IFeedAnalyticsService` tại:

- interface: `Backend/Microservices/Ai.Microservice/src/Application/Abstractions/Feed/IFeedAnalyticsService.cs:6-19`
- implementation: `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:10-259`

### 5.3 Mapping sang normalized AI dashboard models

AI map Feed response sang các model public đang dùng chung với social dashboards khác tại `Backend/Microservices/Ai.Microservice/src/Application/Posts/Models/SocialPlatformPostModels.cs:25-99`.

Các điểm quan trọng:

- `Platform = "feed"` tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:12`
- `Likes`, `Comments`, `Replies`, `TotalInteractions` được map trực tiếp tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:147-163`
- `Views`, `Reach`, `Impressions`, `Shares`, `Reposts`, `Quotes`, `Saves` được để `null` tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:149-160`
- Feed-specific breakdown được chuyển vào `MetricBreakdown` và `AdditionalMetrics` tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:191-215`

### 5.4 Analysis behavior khi Feed không có views

AI giữ cùng factory phân tích nhưng đã hỗ trợ trường hợp thiếu `Views` bằng highlight trung thực hơn tại `Backend/Microservices/Ai.Microservice/src/Application/Posts/SocialPlatformPostAnalysisFactory.cs:7-135`.

Khi `Views == null`:

- `EngagementRateByViews`, `ConversationRateByViews`, `AmplificationRateByViews`, `ApprovalRateByViews` sẽ là `null`
- `PerformanceBand` là `insufficient_data`
- highlight vẫn có thể nói về `tracked interactions`

---

## 6. Public API mới trên AI microservice

AI expose hai endpoint mới:

### 6.1 Feed dashboard summary

`GET /api/Ai/posts/feed/{username}/dashboard-summary`

Source: `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs:199-223`

Query params:

- `postLimit?: number`

### 6.2 Feed post analytics

`GET /api/Ai/posts/feed/posts/{postId}/analytics`

Source: `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs:225-249`

Query params:

- `commentSampleLimit?: number`

### 6.3 Query handlers bên AI

- dashboard summary query: `Backend/Microservices/Ai.Microservice/src/Application/Posts/Queries/GetFeedDashboardSummaryQuery.cs:9-38`
- post analytics query: `Backend/Microservices/Ai.Microservice/src/Application/Posts/Queries/GetFeedPostAnalyticsQuery.cs:9-38`

Các handler này chỉ orchestrate tới `IFeedAnalyticsService`, giữ AI Application layer mỏng và nhất quán với kiến trúc hiện có.

---

## 7. So sánh với các dashboard APIs social hiện có

Các dashboard social hiện có của AI nằm tại `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PostsController.cs:94-197`.

### 7.1 So sánh route family

| Nhu cầu | Dashboard social hiện có | Feed dashboard mới |
|---|---|---|
| Dashboard summary | `GET /api/Ai/posts/social/{socialMediaId}/dashboard-summary` | `GET /api/Ai/posts/feed/{username}/dashboard-summary` |
| Per-post analytics | `GET /api/Ai/posts/social/{socialMediaId}/platform-posts/{platformPostId}/analytics` | `GET /api/Ai/posts/feed/posts/{postId}/analytics` |
| Batch dashboard | `POST /api/Ai/posts/dashboard-summary/batch` | chưa có batch feed riêng |

### 7.2 So sánh identity model

| Chiều | Social dashboards hiện có | Feed dashboard mới |
|---|---|---|
| Account identity | `socialMediaId` | `username` |
| Post identity | `platformPostId` string từ provider | `postId` là `Guid` của Feed |
| Source system | external platform account | internal feed profile |

### 7.3 So sánh nguồn dữ liệu

| Chiều | Social dashboards hiện có | Feed dashboard mới |
|---|---|---|
| Source | Facebook / Instagram / TikTok / Threads APIs | Feed database + counters + public resource/profile services |
| Data freshness | phụ thuộc provider API | phụ thuộc dữ liệu nội bộ hiện có trong hệ thống |
| Exposure metrics | thường có một phần | hiện chưa có |

### 7.4 So sánh response contract

| Chiều | Social dashboards hiện có | Feed dashboard mới |
|---|---|---|
| Public response model | `SocialPlatformDashboardSummaryResponse` / `SocialPlatformPostAnalyticsResponse` | dùng lại chính các model này |
| Envelope | `Result<T>` / `ProblemDetails` | giữ nguyên `Result<T>` / `ProblemDetails` |
| Platform field | `facebook`, `instagram`, `tiktok`, `threads` | `feed` |

### 7.5 So sánh metrics availability

| Metric | Social dashboards hiện có | Feed dashboard mới |
|---|---|---|
| `likes` | có | có |
| `comments` | có | có |
| `replies` | có hoặc tùy provider | có |
| `totalInteractions` | có | có |
| `views` | thường có | `null` |
| `reach` | có với một số provider | `null` |
| `impressions` | có với một số provider | `null` |
| `shares` | có với một số provider | `null` |
| `reposts` | có với một số provider | `null` |
| `quotes` | có với một số provider | `null` |
| `saves` | có với một số provider | `null` |
| `topLevelComments` | thường không phải field first-class | có trong `MetricBreakdown` / `AdditionalMetrics` |
| `hashtagCount` | tùy provider | có trong `MetricBreakdown` / `AdditionalMetrics` |
| `mediaCount` | tùy provider | có trong `MetricBreakdown` / `AdditionalMetrics` |

---

## 8. Khác biệt quan trọng frontend phải biết

Frontend không nên giả định Feed dashboard giống hệt external dashboard.

### 8.1 Không có view-based rate thực

Do Feed không có `Views`, frontend phải chấp nhận:

- rate-based cards có thể hiển thị `N/A`
- chart nào phụ thuộc `Views`, `Reach`, `Impressions` cần fallback
- `PerformanceBand` có thể là `insufficient_data`

### 8.2 Feed có nhiều insight nội bộ hơn ở discussion depth

Feed có thể tận dụng:

- `topLevelComments`
- `replies`
- `totalDiscussion`
- `mediaCount`
- `hashtagCount`

Các field này nằm trong `MetricBreakdown` hoặc `AdditionalMetrics` sau khi normalize tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Feed/FeedAnalyticsGrpcClient.cs:191-215`.

### 8.3 Route semantics khác nhau

Các dashboard social dùng `socialMediaId`, còn Feed dashboard dùng `username` hoặc `postId`. Frontend adapter không nên ép các route này về cùng một URL pattern tuyệt đối; chỉ nên unify ở data layer và UI layer.

---

## 9. Hướng dẫn tích hợp trong ứng dụng Vite + React + TypeScript

Phần này giả định bạn đang gọi AI public API qua API Gateway, và auth đi qua cookie hoặc bearer token giống các màn hình backend khác.

### 9.1 Cấu trúc thư mục gợi ý

```text
src/
  features/
    dashboard/
      api/
        dashboardApi.ts
      hooks/
        useSocialDashboardSummaryQuery.ts
        useSocialPostAnalyticsQuery.ts
        useFeedDashboardSummaryQuery.ts
        useFeedPostAnalyticsQuery.ts
      types/
        dashboard.types.ts
      adapters/
        dashboardMetricAdapters.ts
      components/
        DashboardSummaryCards.tsx
        DashboardPostList.tsx
        DashboardPostAnalyticsPanel.tsx
        DashboardMetricFallback.tsx
        FeedDiscussionBreakdown.tsx
```

### 9.2 Shared contracts nên reuse

Từ kinh nghiệm đang ghi trong `docs/feed-microservice-api.md:294-323`, nên tiếp tục giữ `Result<T>` và `ProblemDetailsResponse` để đồng nhất toàn hệ thống.

```ts
export interface Result<T> {
  isSuccess: boolean;
  isFailure: boolean;
  value: T;
  error?: {
    code: string;
    message: string;
  };
}

export interface ProblemDetailsResponse {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
```

### 9.3 TypeScript models cho dashboard

```ts
export interface SocialPlatformPostStatsResponse {
  views: number | null;
  reach: number | null;
  impressions: number | null;
  likes: number | null;
  comments: number | null;
  replies: number | null;
  shares: number | null;
  reposts: number | null;
  quotes: number | null;
  totalInteractions: number;
  saves: number | null;
  reactionBreakdown?: Record<string, number> | null;
  metricBreakdown?: Record<string, number> | null;
}

export interface SocialPlatformPostAnalysisResponse {
  engagementRateByViews: number | null;
  conversationRateByViews: number | null;
  amplificationRateByViews: number | null;
  approvalRateByViews: number | null;
  performanceBand: string;
  highlights: string[];
}

export interface SocialPlatformPostSummaryResponse {
  platformPostId: string;
  title: string | null;
  text: string | null;
  description: string | null;
  mediaType: string | null;
  mediaUrl: string | null;
  thumbnailUrl: string | null;
  permalink: string | null;
  shareUrl: string | null;
  embedUrl: string | null;
  durationSeconds: number | null;
  publishedAt: string | null;
  stats: SocialPlatformPostStatsResponse | null;
}

export interface SocialPlatformDashboardPostResponse {
  post: SocialPlatformPostSummaryResponse;
  analysis: SocialPlatformPostAnalysisResponse | null;
}

export interface SocialPlatformAccountInsightsResponse {
  accountId: string | null;
  accountName: string | null;
  username: string | null;
  followers: number | null;
  following: number | null;
  mediaCount: number | null;
  metadata?: Record<string, string> | null;
}

export interface SocialPlatformCommentResponse {
  id: string;
  text: string | null;
  authorId: string | null;
  authorName: string | null;
  authorUsername: string | null;
  createdAt: string | null;
  likeCount: number | null;
  replyCount: number | null;
  permalink: string | null;
}

export interface SocialPlatformDashboardSummaryResponse {
  socialMediaId: string;
  platform: string;
  fetchedPostCount: number;
  hasMorePosts: boolean;
  nextCursor: string | null;
  latestPublishedPostId: string | null;
  latestPublishedAt: string | null;
  aggregatedStats: SocialPlatformPostStatsResponse;
  latestAnalysis: SocialPlatformPostAnalysisResponse | null;
  accountInsights: SocialPlatformAccountInsightsResponse | null;
  posts: SocialPlatformDashboardPostResponse[];
}

export interface SocialPlatformPostAnalyticsResponse {
  socialMediaId: string;
  platform: string;
  platformPostId: string;
  post: SocialPlatformPostSummaryResponse;
  stats: SocialPlatformPostStatsResponse;
  analysis: SocialPlatformPostAnalysisResponse;
  retrievedAt: string;
  accountInsights: SocialPlatformAccountInsightsResponse | null;
  commentSamples?: SocialPlatformCommentResponse[] | null;
  additionalMetrics?: Record<string, number> | null;
}
```

### 9.4 API client gợi ý

Giữ pattern fetch chuẩn như feed docs đang gợi ý tại `docs/feed-microservice-api.md:423-445`.

```ts
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:2406';

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
    ...init,
  });

  if (!response.ok) {
    const errorBody = await response.json().catch(() => null);
    throw errorBody ?? new Error('Request failed');
  }

  return response.json() as Promise<T>;
}
```

### 9.5 Dashboard API module

```ts
import type {
  Result,
  SocialPlatformDashboardSummaryResponse,
  SocialPlatformPostAnalyticsResponse,
} from '../types/dashboard.types';
import { apiFetch } from '@/shared/api/apiFetch';

export function getSocialDashboardSummary(socialMediaId: string, postLimit?: number) {
  const query = postLimit ? `?postLimit=${postLimit}` : '';
  return apiFetch<Result<SocialPlatformDashboardSummaryResponse>>(
    `/api/Ai/posts/social/${socialMediaId}/dashboard-summary${query}`,
  );
}

export function getSocialPostAnalytics(
  socialMediaId: string,
  platformPostId: string,
  refresh = false,
) {
  return apiFetch<Result<SocialPlatformPostAnalyticsResponse>>(
    `/api/Ai/posts/social/${socialMediaId}/platform-posts/${platformPostId}/analytics?refresh=${refresh}`,
  );
}

export function getFeedDashboardSummary(username: string, postLimit?: number) {
  const query = postLimit ? `?postLimit=${postLimit}` : '';
  return apiFetch<Result<SocialPlatformDashboardSummaryResponse>>(
    `/api/Ai/posts/feed/${encodeURIComponent(username)}/dashboard-summary${query}`,
  );
}

export function getFeedPostAnalytics(postId: string, commentSampleLimit?: number) {
  const query = commentSampleLimit ? `?commentSampleLimit=${commentSampleLimit}` : '';
  return apiFetch<Result<SocialPlatformPostAnalyticsResponse>>(
    `/api/Ai/posts/feed/posts/${postId}/analytics${query}`,
  );
}
```

### 9.6 React Query hooks

```ts
import { useQuery } from '@tanstack/react-query';
import {
  getFeedDashboardSummary,
  getFeedPostAnalytics,
} from '../api/dashboardApi';

export function useFeedDashboardSummaryQuery(username: string, postLimit = 5) {
  return useQuery({
    queryKey: ['dashboard', 'feed', username, postLimit],
    queryFn: () => getFeedDashboardSummary(username, postLimit),
    enabled: Boolean(username),
    select: (result) => result.value,
  });
}

export function useFeedPostAnalyticsQuery(postId: string, commentSampleLimit = 5) {
  return useQuery({
    queryKey: ['dashboard', 'feed', 'post', postId, commentSampleLimit],
    queryFn: () => getFeedPostAnalytics(postId, commentSampleLimit),
    enabled: Boolean(postId),
    select: (result) => result.value,
  });
}
```

### 9.7 UI reuse strategy với dashboards hiện có

Khuyến nghị:

- tiếp tục dùng chung `DashboardSummaryCards`, `DashboardPostList`, `DashboardPostAnalyticsPanel`
- chỉ thêm logic adapter nếu `platform === 'feed'`
- render fallback cho view-based metrics
- render thêm block discussion metrics khi là Feed

Ví dụ adapter:

```ts
export function getDashboardPrimaryMetrics(summary: SocialPlatformDashboardSummaryResponse) {
  const stats = summary.aggregatedStats;

  return [
    { key: 'likes', label: 'Likes', value: stats.likes ?? 0 },
    { key: 'comments', label: 'Comments', value: stats.comments ?? 0 },
    { key: 'replies', label: 'Replies', value: stats.replies ?? 0 },
    { key: 'interactions', label: 'Interactions', value: stats.totalInteractions },
    {
      key: 'views',
      label: 'Views',
      value: stats.views,
      unavailable: stats.views == null,
    },
  ];
}

export function getFeedDiscussionMetrics(stats: SocialPlatformPostStatsResponse) {
  return {
    topLevelComments: stats.metricBreakdown?.topLevelComments ?? 0,
    replies: stats.metricBreakdown?.replies ?? 0,
    mediaCount: stats.metricBreakdown?.mediaCount ?? 0,
    hashtagCount: stats.metricBreakdown?.hashtagCount ?? 0,
  };
}
```

### 9.8 Fallback rendering bắt buộc cho Feed

```tsx
function MetricValue({ value }: { value: number | null | undefined }) {
  if (value == null) {
    return <span>N/A</span>;
  }

  return <span>{value.toLocaleString()}</span>;
}
```

Dùng fallback này cho:

- `views`
- `reach`
- `impressions`
- `shares`
- `reposts`
- `quotes`
- `saves`
- các rate-by-views trong analysis

### 9.9 Component riêng nên có cho Feed

Ngoài UI reuse từ dashboard social hiện tại, nên thêm 1 block nhỏ cho Feed:

```tsx
type FeedDiscussionBreakdownProps = {
  stats: SocialPlatformPostStatsResponse;
  additionalMetrics?: Record<string, number> | null;
};

export function FeedDiscussionBreakdown({ stats, additionalMetrics }: FeedDiscussionBreakdownProps) {
  const topLevelComments = stats.metricBreakdown?.topLevelComments ?? additionalMetrics?.topLevelComments ?? 0;
  const replies = stats.metricBreakdown?.replies ?? additionalMetrics?.replies ?? 0;
  const mediaCount = stats.metricBreakdown?.mediaCount ?? additionalMetrics?.mediaCount ?? 0;
  const hashtagCount = stats.metricBreakdown?.hashtagCount ?? additionalMetrics?.hashtagCount ?? 0;

  return (
    <section>
      <h3>Feed discussion breakdown</h3>
      <ul>
        <li>Top-level comments: {topLevelComments}</li>
        <li>Replies: {replies}</li>
        <li>Media count: {mediaCount}</li>
        <li>Hashtag count: {hashtagCount}</li>
      </ul>
    </section>
  );
}
```

### 9.10 Loading and error handling

Vì backend vẫn giữ đúng contract `Result<T>` / `ProblemDetails`, frontend nên:

- dùng React Query `isLoading`, `isFetching`, `isError`
- map `ProblemDetails.detail` thành message chính
- nếu 401 thì redirect hoặc hiển thị login gate
- nếu `platform === 'feed'` và các metric exposure là `null`, không coi đó là lỗi

Ví dụ:

```ts
export function getProblemDetailMessage(error: unknown): string {
  if (typeof error === 'object' && error && 'detail' in error) {
    return String((error as { detail?: string }).detail ?? 'Request failed');
  }

  if (error instanceof Error) {
    return error.message;
  }

  return 'Request failed';
}
```

---

## 10. Khuyến nghị triển khai frontend theo từng bước

### Bước 1
Tái sử dụng toàn bộ `dashboard.types.ts` đang dùng cho social dashboards hiện tại, chỉ mở rộng chỗ nào cần cho `MetricBreakdown` / `AdditionalMetrics`.

### Bước 2
Thêm `dashboardApi.ts` với 2 hàm mới:

- `getFeedDashboardSummary`
- `getFeedPostAnalytics`

### Bước 3
Thêm 2 hooks mới:

- `useFeedDashboardSummaryQuery`
- `useFeedPostAnalyticsQuery`

### Bước 4
Tại layer router/page, tạo page hoặc tab Feed Dashboard dùng cùng container UI như social dashboards.

### Bước 5
Tại chart/card layer, thêm fallback `N/A` cho các metric exposure nếu `platform === 'feed'`.

### Bước 6
Tại analytics detail panel, render `FeedDiscussionBreakdown` dựa trên `metricBreakdown` và `additionalMetrics`.

---

## 11. Local environment notes

### 11.1 Feed gRPC port

Feed đang mở gRPC HTTP/2 trên port `5008` tại `Backend/Microservices/Feed.Microservice/src/WebApi/Program.cs:24-27`.

### 11.2 Docker Compose wiring

Compose đã có `FeedService__GrpcUrl: http://feed-microservice:5008` cho Feed service container tại `Backend/Compose/docker-compose.yml:188-191`.

### 11.3 AI gRPC client proto compile

AI infrastructure compile proto client từ `feed_analytics.proto` tại `Backend/Microservices/Ai.Microservice/src/Infrastructure/Infrastructure.csproj:41-44`.

---

## 12. Automated verification đã có

### Feed tests

Feed analytics query tests đã được thêm để verify:

- dashboard aggregation
- profile stats
- discussion breakdown
- comment samples

Test file: `Backend/Microservices/Feed.Microservice/test/FeedAnalyticsQueryTests.cs`

### AI tests

AI tests đã được thêm để verify:

- query handlers delegate đúng tới `IFeedAnalyticsService`
- response contract giữ nguyên normalized shape cho FE

Test file: `Backend/Microservices/Ai.Microservice/test/Application/Posts/Queries/FeedAnalyticsQueryTests.cs`

---

## 13. Kết luận

Thiết kế hiện tại phù hợp khi bạn muốn:

- giữ ranh giới service sạch
- không cho AI đọc trực tiếp database của Feed
- giữ AI là lớp public analytics thống nhất cho FE
- tái sử dụng UI dashboards hiện có trong Vite + React + TypeScript
- mở rộng dần Feed analytics sau này mà không phá vỡ contract frontend hiện tại

Nếu sau này product cần parity cao hơn với social dashboards bên ngoài, phase tiếp theo nên bổ sung **exposure tracking pipeline** trong Feed thay vì suy diễn từ các counters hiện tại.
