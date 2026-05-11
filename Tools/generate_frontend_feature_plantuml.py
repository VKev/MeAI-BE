#!/usr/bin/env python3
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import shutil


REPO_ROOT = Path(__file__).resolve().parents[1]
PLANTUML_ROOT = REPO_ROOT / "plantuml"
FEATURES_DIR = PLANTUML_ROOT / "features"
TRACKER_PATH = REPO_ROOT / "plans" / "frontend-feature-diagram-tracker.md"
README_PATH = PLANTUML_ROOT / "README.md"


@dataclass(frozen=True)
class Feature:
    slug: str
    title: str
    actor: str
    repos: tuple[str, ...]
    routes: tuple[str, ...]
    frontend_classes: tuple[str, ...]
    backend_classes: tuple[str, ...]
    integrations: tuple[str, ...]
    has_async_delivery: bool
    sequence_body: str


def class_block(feature: Feature) -> str:
    frontend_lines = "\n".join(
        f'class "{name}" as FE{idx} <<Frontend>> {{\n  +runMainAction()\n  +refreshVisibleState()\n  -localState\n  -validationState\n}}'
        for idx, name in enumerate(feature.frontend_classes, start=1)
    )
    backend_lines = "\n".join(
        f'class "{name}" as BE{idx} <<Backend>> {{\n  +handleRequest()\n  +coordinateFlow()\n  -domainRules\n  -integrationPorts\n}}'
        for idx, name in enumerate(feature.backend_classes, start=1)
    )
    integration_lines = "\n".join(
        f'class "{name}" as INT{idx} <<Integration>> {{\n  +sendOrReceive()\n  +returnResult()\n  -transportConfig\n  -retryRules\n}}'
        for idx, name in enumerate(feature.integrations, start=1)
    )

    relations: list[str] = []
    if len(feature.frontend_classes) >= 2:
        for idx in range(1, len(feature.frontend_classes)):
            relations.append(f"FE{idx} --> FE{idx + 1}")
    if feature.frontend_classes and feature.backend_classes:
        relations.append("FE1 --> BE1")
    if len(feature.backend_classes) >= 2:
        for idx in range(1, len(feature.backend_classes)):
            relations.append(f"BE{idx} --> BE{idx + 1}")
    if feature.backend_classes and feature.integrations:
        relations.append("BE1 --> INT1")
        for idx in range(1, min(len(feature.backend_classes), len(feature.integrations))):
            relations.append(f"BE{idx + 1} --> INT{idx + 1}")
    if len(feature.integrations) >= 2:
        for idx in range(1, len(feature.integrations)):
            relations.append(f"INT{idx} --> INT{idx + 1}")

    integration_package = "Integrations And Delivery" if feature.has_async_delivery else "External Systems And Storage"

    body_parts = [
        "@startuml",
        f"title {feature.title}",
        "skinparam shadowing false",
        "skinparam classAttributeIconSize 0",
        "skinparam packageStyle rectangle",
        "hide empty members",
        'package "Frontend" {',
        frontend_lines,
        "}",
        'package "Backend Services" {',
        backend_lines,
        "}",
        f'package "{integration_package}" {{',
        integration_lines,
        "}",
        *relations,
        "@enduml",
    ]
    return "\n".join(part for part in body_parts if part.strip())


def sequence_block(feature: Feature) -> str:
    parts = [
        "@startuml",
        f"title {feature.title}",
        "autonumber",
        "skinparam shadowing false",
        "skinparam sequenceMessageAlign center",
        f"actor {feature.actor}",
        'box "Frontend" #E8F0FE',
        'participant "Visible Screen" as Screen',
        'participant "Route Action Or Hook" as Hook',
        'participant "Frontend Store And Query Cache" as Store',
        "end box",
        'box "Gateway And Backend" #FFF4E5',
        'participant "API Gateway" as Gateway',
        'participant "Primary Service" as PrimaryService',
        'participant "Supporting Service" as SupportingService',
        'participant "Persistence And Domain Rules" as DomainLayer',
        "end box",
    ]
    if feature.has_async_delivery:
        parts.extend(
            [
                'box "Async And Delivery" #E8F5E9',
                'participant "gRPC Or RabbitMQ Channel" as AsyncBus',
                'participant "Notification Service And Hub" as NotificationHub',
                "end box",
            ]
        )
    parts.extend(["", feature.sequence_body.strip(), "@enduml"])
    return "\n".join(parts)


def feature_file(feature: Feature) -> str:
    return f"{class_block(feature)}\n\n{sequence_block(feature)}\n"


FEATURES: tuple[Feature, ...] = (
    Feature(
        slug="meai-user-sign-up-and-email-verification",
        title="User Signs Up With Email Verification",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/auth/sign-up", "/auth/send-verification-code"),
        frontend_classes=("Sign Up Screen", "Sign Up Route Action", "Session Manager"),
        backend_classes=("User Auth Controller", "Registration Application Flow", "Session Cookie Service"),
        integrations=("Verification Mail Sender", "User Database"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : enter profile, email, password and request a code
Screen -> Hook : submit verification-code request
Hook -> Gateway : POST /auth/send-verification-code
Gateway -> PrimaryService : forward registration-code request
PrimaryService -> DomainLayer : validate email format and request type
alt invalid email or missing type
  DomainLayer --> PrimaryService : validation failure
  PrimaryService --> Gateway : 400 problem details
  Gateway --> Hook : error response
  Hook --> Screen : show inline validation state
else request accepted
  DomainLayer -> AsyncBus : ask verification mail sender to deliver code
  AsyncBus -> SupportingService : send verification code
  SupportingService --> DomainLayer : delivery accepted
  DomainLayer --> PrimaryService : code issued
  PrimaryService --> Gateway : success envelope
  Gateway --> Hook : code request accepted
  Hook --> Screen : enable code field and countdown
end
group Complete registration
  User -> Screen : submit form with the received code
  Screen -> Hook : post registration form
  Hook -> Gateway : POST /auth/sign-up
  Gateway -> PrimaryService : forward signup payload
  PrimaryService -> DomainLayer : verify code, create user and assign roles
  alt code expired, duplicated email or weak password
    DomainLayer --> PrimaryService : domain failure
    PrimaryService --> Gateway : 400 problem details
    Gateway --> Hook : failure response
    Hook --> Screen : preserve form state and show error
  else registration succeeds
    DomainLayer -> SupportingService : persist user profile and credentials
    SupportingService --> DomainLayer : user id and roles
    DomainLayer -> PrimaryService : build authenticated session response
    PrimaryService --> Gateway : success with auth cookie
    Gateway --> Hook : success with Set-Cookie
    Hook -> Store : persist current user session
    Store --> Screen : redirect target ready
    Screen --> User : navigate to user dashboard or admin area
  end
end
""",
    ),
    Feature(
        slug="meai-user-sign-in-and-session-refresh",
        title="User Signs In And Frontend Keeps The Session Fresh",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/auth/sign-in", "/api/refresh", "/api/session-check"),
        frontend_classes=("Sign In Screen", "Auth Fetch Client", "Session State Store"),
        backend_classes=("User Auth Controller", "Refresh Token Flow", "Session Server Helpers"),
        integrations=("Redis Refresh Store", "User Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : enter email or username and password
Screen -> Hook : submit sign-in form
Hook -> Gateway : POST /api/User/auth/login
Gateway -> PrimaryService : forward credentials
PrimaryService -> DomainLayer : validate credentials and build access session
alt credentials invalid or account blocked
  DomainLayer --> PrimaryService : authentication failure
  PrimaryService --> Gateway : unauthorized contract
  Gateway --> Hook : login failure
  Hook --> Screen : show error without losing entered values
else credentials valid
  DomainLayer -> SupportingService : create refresh token and cookie set
  SupportingService --> DomainLayer : token persisted
  PrimaryService --> Gateway : authenticated response
  Gateway --> Hook : cookies and user payload
  Hook -> Store : cache current user and role
  Store --> Screen : render authenticated shell
end
opt Later API call receives expired access token
  Hook -> Gateway : call protected API
  Gateway --> Hook : 401 unauthorized
  Hook -> PrimaryService : call refresh helper route
  PrimaryService -> SupportingService : validate refresh token in Redis
  alt refresh token invalid
    SupportingService --> PrimaryService : reject refresh
    PrimaryService --> Hook : clear auth state
    Hook -> Store : remove session
    Store --> Screen : redirect to sign-in
  else refresh token valid
    SupportingService --> PrimaryService : new access cookie
    PrimaryService --> Hook : refreshed session
    Hook -> Store : update current user session
    Hook -> Gateway : replay original protected request
    Gateway -> PrimaryService : protected endpoint executes normally
    PrimaryService --> Gateway : business response
    Gateway --> Hook : final payload
    Hook --> Screen : continue user action seamlessly
  end
end
""",
    ),
    Feature(
        slug="meai-user-forgot-and-reset-password",
        title="User Resets A Forgotten Password",
        actor="User",
        repos=("MeAI-FE", "MeAI-Social-Platform"),
        routes=("/auth/forgot-password", "/auth/send-verification-code"),
        frontend_classes=("Forgot Password Screen", "Reset Password Screen", "Auth Mutation Client"),
        backend_classes=("User Auth Controller", "Password Reset Application Flow", "Credential Service"),
        integrations=("Verification Mail Sender", "User Database"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : ask for a reset code with their email
Screen -> Hook : submit forgot-password request
Hook -> Gateway : POST /auth/send-verification-code with forgot-password type
Gateway -> PrimaryService : forward reset-code request
PrimaryService -> DomainLayer : validate account and issue verification code
alt account not eligible or email missing
  DomainLayer --> PrimaryService : validation failure
  PrimaryService --> Gateway : 400 problem details
  Gateway --> Hook : failure
  Hook --> Screen : show request error
else code issued
  DomainLayer -> AsyncBus : trigger mail delivery
  AsyncBus -> SupportingService : send password-reset code
  SupportingService --> DomainLayer : delivery accepted
  PrimaryService --> Gateway : success
  Gateway --> Hook : reset code sent
  Hook --> Screen : open code and new-password step
end
group Submit the new password
  User -> Screen : enter code, new password and confirmation
  Screen -> Hook : submit reset-password form
  Hook -> Gateway : POST /api/User/auth/reset-password
  Gateway -> PrimaryService : forward reset payload
  PrimaryService -> DomainLayer : verify code and password policy
  alt code expired or passwords invalid
    DomainLayer --> PrimaryService : domain error
    PrimaryService --> Gateway : 400 problem details
    Gateway --> Hook : failure
    Hook --> Screen : show reason and keep the user on the form
  else reset succeeds
    DomainLayer -> SupportingService : replace stored password hash
    SupportingService --> DomainLayer : password updated
    PrimaryService --> Gateway : success response
    Gateway --> Hook : reset confirmed
    Hook --> Screen : redirect user back to sign-in with success feedback
  end
end
""",
    ),
    Feature(
        slug="meai-user-connects-social-account-and-returns-to-workspace",
        title="User Connects A Social Account And Returns To The Workspace Flow",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/user/social-links", "/auth/facebook.callback", "/auth/instagram.callback", "/auth/threads.callback", "/auth/tiktok.callback"),
        frontend_classes=("Social Links Screen", "OAuth Callback Screen", "Workspace Auto Link Helper"),
        backend_classes=("User Social Authorization Controller", "Social Media Application Flow", "Workspace Link Flow"),
        integrations=("Facebook Or Instagram Or TikTok Or Threads API", "Social Media Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : choose a platform to connect
Screen -> Hook : request authorization URL
Hook -> Gateway : GET social authorize endpoint
Gateway -> PrimaryService : forward authorize request
PrimaryService -> DomainLayer : build provider state and callback url
DomainLayer -> SupportingService : store provider handshake metadata
SupportingService --> DomainLayer : state persisted
PrimaryService --> Gateway : authorization url
Gateway --> Hook : auth url response
Hook --> Screen : redirect browser to provider
group Provider callback returns to frontend
  User -> Screen : finish consent on the social network
  Screen -> Hook : load callback route with code and state
  Hook -> Gateway : POST provider callback via FE client
  Gateway -> PrimaryService : exchange code for account tokens
  PrimaryService -> SupportingService : fetch profile or pages and persist social-media records
  SupportingService --> PrimaryService : linked accounts saved
  alt callback fails or provider rejects consent
    PrimaryService --> Gateway : failure contract
    Gateway --> Hook : callback failed
    Hook --> Screen : show error and send user back to Social Links
  else callback succeeds
    PrimaryService --> Gateway : connected account payload
    Gateway --> Hook : success
    opt User started OAuth from a publish dialog
      Hook -> Store : read stashed workspace continuation
      Store -> Gateway : create workspace-social-media links for matching accounts
      Gateway -> SupportingService : persist workspace links
      SupportingService --> Gateway : links saved
      Gateway --> Store : continuation restored
    end
    Hook --> Screen : redirect user to Social Links or back to the post-builder publish dialog
  end
end
""",
    ),
    Feature(
        slug="meai-user-manages-social-links",
        title="User Reviews, Refreshes And Disconnects Social Links",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/user/social-links",),
        frontend_classes=("Social Links Screen", "Social Media Query Cache", "Disconnect Dialog"),
        backend_classes=("User Social Media Controller", "Social Media Management Flow", "Subscription Guard"),
        integrations=("Social Media Database", "Workspace Link Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open Social Links
Screen -> Hook : start account query
Hook -> Gateway : GET /api/User/social-medias
Gateway -> PrimaryService : list connected accounts
PrimaryService -> DomainLayer : read connected social-media rows
DomainLayer --> PrimaryService : accounts grouped by platform
PrimaryService --> Gateway : account list
Gateway --> Hook : list response
Hook -> Store : cache account inventory
Store --> Screen : render connected and disconnected states
opt User presses Sync Now
  Screen -> Hook : refetch the list
  Hook -> Gateway : GET social-media list again
  Gateway -> PrimaryService : reload latest rows
  PrimaryService --> Gateway : refreshed list
  Gateway --> Hook : new payload
  Hook -> Store : replace cached accounts
  Store --> Screen : show the latest connection state
end
group Disconnect an account
  User -> Screen : confirm disconnect for one linked account
  Screen -> Hook : call delete social-media endpoint
  Hook -> Gateway : DELETE /api/User/social-medias/{id}
  Gateway -> PrimaryService : remove account
  PrimaryService -> SupportingService : validate subscription rules and detach workspace links
  alt subscription rule blocks the action
    SupportingService --> PrimaryService : business failure
    PrimaryService --> Gateway : problem details
    Gateway --> Hook : failure
    Hook --> Screen : show toast with the precise reason
  else account removed
    SupportingService --> PrimaryService : account and workspace links deleted
    PrimaryService --> Gateway : success
    Gateway --> Hook : mutation success
    Hook -> Store : invalidate social-media queries
    Store --> Screen : refresh visible cards and counts
  end
end
""",
    ),
    Feature(
        slug="meai-user-manages-workspaces",
        title="User Creates, Updates And Removes Workspaces",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/user/workspace", "/workspace/:workspaceId"),
        frontend_classes=("Workspace List Screen", "Workspace Detail Shell", "Workspace Mutation Client"),
        backend_classes=("User Workspace Controller", "Workspace Application Flow", "Workspace Repository"),
        integrations=("Workspace Database", "Workspace Social Media Link Store"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open the workspace area
Screen -> Hook : load workspace list
Hook -> Gateway : GET /api/User/workspaces
Gateway -> PrimaryService : list workspaces for current user
PrimaryService -> DomainLayer : read workspace rows and metadata
DomainLayer --> PrimaryService : workspace collection
PrimaryService --> Gateway : success response
Gateway --> Hook : workspace list
Hook -> Store : cache workspaces
Store --> Screen : render cards and workspace entry points
group Create or update a workspace
  User -> Screen : submit workspace name, type or settings
  Screen -> Hook : call create or update mutation
  Hook -> Gateway : POST or PUT workspace endpoint
  Gateway -> PrimaryService : validate payload and ownership
  PrimaryService -> DomainLayer : persist workspace changes
  alt validation fails
    DomainLayer --> PrimaryService : domain error
    PrimaryService --> Gateway : problem details
    Gateway --> Hook : failure
    Hook --> Screen : show inline error state
  else save succeeds
    DomainLayer --> PrimaryService : workspace saved
    PrimaryService --> Gateway : updated workspace
    Gateway --> Hook : mutation success
    Hook -> Store : invalidate list and detail queries
    Store --> Screen : show refreshed workspace inventory
  end
end
group Delete a workspace
  User -> Screen : confirm workspace deletion
  Screen -> Hook : call delete mutation
  Hook -> Gateway : DELETE /api/User/workspaces/{id}
  Gateway -> PrimaryService : delete workspace
  PrimaryService -> SupportingService : remove workspace-social links and dependent rows
  SupportingService --> PrimaryService : cleanup completed
  PrimaryService --> Gateway : success response
  Gateway --> Hook : deletion success
  Hook -> Store : remove workspace from cache
  Store --> Screen : route user back to the remaining workspace list
end
""",
    ),
    Feature(
        slug="meai-user-library-upload-and-send-to-post-builder",
        title="User Uploads Resources And Sends Them To The Post Builder",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/user/library", "/workspace/:workspaceId/post-builder/:id"),
        frontend_classes=("Library Screen", "Resource Upload Flow", "Post Prepare Client"),
        backend_classes=("User Resource Controller", "AI Post Prepare Controller", "Post Builder Creation Flow"),
        integrations=("S3 Resource Storage", "Resource Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open the library page
Screen -> Hook : load storage usage and resource pages
Hook -> Gateway : GET resources and storage usage
Gateway -> PrimaryService : fetch resource inventory
PrimaryService -> DomainLayer : read user-owned and AI-generated resources
DomainLayer -> SupportingService : build presigned resource links and storage totals
SupportingService --> DomainLayer : enriched resources and quota data
PrimaryService --> Gateway : resource list and quota
Gateway --> Hook : resource payload
Hook -> Store : cache resources and pagination cursors
Store --> Screen : render previews, filters and selection state
group Upload a new resource
  User -> Screen : choose a file from the device
  Screen -> Hook : submit multipart upload
  Hook -> Gateway : POST /api/User/resources
  Gateway -> PrimaryService : validate media type and storage quota
  alt storage limit exceeded or media invalid
    PrimaryService --> Gateway : business failure
    Gateway --> Hook : upload rejected
    Hook --> Screen : show error toast
  else upload accepted
    PrimaryService -> SupportingService : store file in S3 and persist metadata
    SupportingService --> PrimaryService : resource id and presigned link
    PrimaryService --> Gateway : upload success
    Gateway --> Hook : resource created
    Hook -> Store : invalidate resource queries
    Store --> Screen : append the new item to the library
  end
end
group Send selected resources into a post-builder
  User -> Screen : pick resources and choose a workspace
  Screen -> Hook : fetch available workspaces and submit post-prepare payload
  Hook -> Gateway : POST /api/AiGeneration/post-prepare
  Gateway -> PrimaryService : create post-builder and seed draft posts
  PrimaryService -> DomainLayer : validate builder resources and social targets
  DomainLayer -> SupportingService : persist post-builder with workspace relation
  SupportingService --> DomainLayer : post-builder id ready
  PrimaryService --> Gateway : builder id and workspace id
  Gateway --> Hook : preparation success
  Hook -> Store : keep selected resources until navigation completes
  Store --> Screen : navigate user into the matching post-builder screen
end
""",
    ),
    Feature(
        slug="meai-user-generates-images-and-videos-with-ai",
        title="User Generates Images Or Videos From The AI Workspace",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/ai-generation/:sessionId/:mode?",),
        frontend_classes=("AI Generation Screen", "Generation Hook", "Notification Hub Hook"),
        backend_classes=("AI Chat Controller", "AI Generation Application Flow", "Coin Debit Flow"),
        integrations=("RabbitMQ Generation Queue", "Notification Hub"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : open the AI generation workspace
Screen -> Hook : fetch default configuration for model and aspect ratio
Hook -> Gateway : GET /api/User/config
Gateway -> SupportingService : load active user configuration
SupportingService --> Gateway : config payload
Gateway --> Hook : config response
Hook -> Store : initialize prompt and media options
Store --> Screen : show image or video controls
group Submit a generation request
  User -> Screen : write prompt and choose model settings
  Screen -> Hook : submit create image or create video chat
  Hook -> Gateway : POST /api/Ai/chats/image or /api/Ai/chats/video
  Gateway -> PrimaryService : create generation chat
  PrimaryService -> SupportingService : debit coins through user billing
  alt coin balance is insufficient
    SupportingService --> PrimaryService : reject debit
    PrimaryService --> Gateway : business failure
    Gateway --> Hook : generation rejected
    Hook --> Screen : show payment or balance error
  else coins debited
    SupportingService --> PrimaryService : debit accepted
    PrimaryService -> DomainLayer : persist chat and enqueue generation work
    DomainLayer -> AsyncBus : publish generation-started message
    AsyncBus --> DomainLayer : accepted
    PrimaryService --> Gateway : accepted response with chat correlation
    Gateway --> Hook : request accepted
    Hook -> Store : place pending item into the visible workspace chat list
    Store --> Screen : show pending skeleton immediately
  end
end
group Background completion returns to the same frontend
  AsyncBus -> PrimaryService : generation completed or failed event
  PrimaryService -> SupportingService : persist generated resources or failure metadata
  SupportingService -> NotificationHub : push notification request
  NotificationHub --> Store : SignalR NotificationReceived event
  alt generation failed
    Store --> Screen : show failure toast and stop the pending spinner
  else generation completed
    Store -> Hook : invalidate workspace chats
    Hook -> Gateway : GET refreshed chat history and resources
    Gateway -> PrimaryService : reload chat session
    PrimaryService --> Gateway : updated chat and resource payload
    Gateway --> Hook : refreshed data
    Hook -> Store : replace pending state with the final media result
    Store --> Screen : show the generated image or video in place
  end
end
""",
    ),
    Feature(
        slug="meai-user-edits-a-post-builder-with-auto-save",
        title="User Edits A Post Builder And Auto Save Keeps Every Draft Bucket In Sync",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/workspace/:workspaceId/post-builder/:id",),
        frontend_classes=("Post Builder Screen", "Post Builder Hydration Hook", "Auto Save Hook"),
        backend_classes=("AI Post Builder Controller", "AI Post Controller", "Resource Hydration Flow"),
        integrations=("User Resource gRPC", "AI Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open a post-builder from the workspace
Screen -> Hook : fetch builder detail and workspace detail
Hook -> Gateway : GET /api/Ai/post-builders/{id} and workspace detail
Gateway -> PrimaryService : load post-builder
PrimaryService -> SupportingService : call user-resource service for hydrated presigned media
SupportingService --> PrimaryService : hydrated media and builder resources
PrimaryService -> DomainLayer : assemble per-platform and per-mode draft state
DomainLayer --> PrimaryService : post-builder detail model
PrimaryService --> Gateway : builder response
Gateway --> Hook : builder payload
Hook -> Store : hydrate captions, media buckets and publish states
Store --> Screen : render content editor and previews
group User changes text or media
  User -> Screen : edit caption, switch mode or select media
  Screen -> Store : update in-memory platform bucket state
  Store -> Hook : debounce auto-save over changed buckets
  group Auto-save evaluates each changed bucket
    Hook -> Gateway : POST create draft or PUT update existing draft
    Gateway -> PrimaryService : save one platform and mode bucket
    PrimaryService -> DomainLayer : skip buckets that are already published or in-flight
    alt bucket is new
      DomainLayer -> SupportingService : create child post bound to the post-builder
      SupportingService --> DomainLayer : new post id
    else bucket already exists
      DomainLayer -> SupportingService : update content, post type and resource list
      SupportingService --> DomainLayer : draft updated
    end
    DomainLayer --> PrimaryService : latest snapshot key saved
    PrimaryService --> Gateway : draft save success
    Gateway --> Hook : save completed
  end
  Hook -> Store : mark snapshot as saved and keep editing available
  Store --> Screen : preserve a continuous editing flow without a blocking reload
end
""",
    ),
    Feature(
        slug="meai-user-publishes-a-post-builder-and-receives-realtime-status",
        title="User Publishes Or Unpublishes A Draft And The Frontend Tracks Every Target In Real Time",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/workspace/:workspaceId/post-builder/:id",),
        frontend_classes=("Publish Dialog", "Notification Hub Hook", "Post Builder Store"),
        backend_classes=("AI Post Controller", "Publish To Target Consumer", "Notification API"),
        integrations=("User Social Media gRPC", "Feed Publish gRPC", "RabbitMQ And SignalR"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : open the publish dialog from the post-builder
Screen -> Hook : load connected accounts and workspace-linked accounts
Hook -> Gateway : GET social-media list and workspace-linked list
Gateway -> SupportingService : return account inventory and existing workspace links
SupportingService --> Gateway : linked and available accounts
Gateway --> Hook : account payload
Hook -> Store : preselect accounts already linked to the workspace
Store --> Screen : render publish targets and scheduling choices
group User confirms publish now or schedule later
  User -> Screen : choose accounts and confirm publish
  Screen -> Hook : sync current draft buckets before enqueueing
  group Preflight and local save
    Hook -> Gateway : create missing child post or update existing draft posts
    Gateway -> PrimaryService : persist latest content and resources
    PrimaryService --> Gateway : latest draft state saved
    Gateway --> Hook : ready for publish
  end
  Hook -> Gateway : POST /api/Ai/posts/publish or schedule endpoint
  Gateway -> PrimaryService : create publish requests per social-media target
  PrimaryService -> DomainLayer : validate platform media rules and builder ownership
  alt platform or media validation fails
    DomainLayer --> PrimaryService : reject target
    PrimaryService --> Gateway : validation error
    Gateway --> Hook : publish rejected
    Hook --> Screen : show precise platform error
  else publish accepted
    DomainLayer -> AsyncBus : enqueue one target per selected account
    AsyncBus --> DomainLayer : queue accepted
    PrimaryService --> Gateway : 202 accepted
    Gateway --> Hook : publish accepted
    Hook -> Store : mark selected buckets as publishing
    Store --> Screen : close dialog and show publishing banners immediately
  end
end
group Background target processing
  AsyncBus -> PrimaryService : deliver publish-to-target work
  PrimaryService -> SupportingService : load account tokens and media resources through gRPC
  SupportingService --> PrimaryService : account credentials and publishable resources ready
  PrimaryService -> DomainLayer : publish to Facebook, Instagram, TikTok or Threads
  opt User also enabled MeAI feed publication
    DomainLayer -> SupportingService : mirror the AI post into the Feed service over gRPC
    SupportingService --> DomainLayer : feed mirror created
  end
  alt one target fails
    DomainLayer -> NotificationHub : emit target-failed notification
  else one target succeeds
    DomainLayer -> NotificationHub : emit target-completed notification
  end
  opt all targets in the batch are now finished
    DomainLayer -> NotificationHub : emit batch-completed notification
  end
end
group Frontend reacts to SignalR events from the same flow
  NotificationHub --> Hook : NotificationReceived
  Hook -> Store : invalidate notifications, post-builder and posts queries
  alt target failed
    Store --> Screen : show failure toast and keep the bucket editable after refresh
  else target or batch completed
    Store -> Gateway : refetch post-builder and posts
    Gateway -> PrimaryService : reload the latest publish state
    PrimaryService --> Gateway : published and failed targets with timestamps
    Gateway --> Store : refreshed data
    Store --> Screen : update banners, target chips and published cards in one continuous loop
  end
end
opt User later unpublishes a live post
  User -> Screen : confirm unpublish
  Screen -> Hook : POST unpublish request
  Hook -> Gateway : /api/Ai/posts/{id}/unpublish
  Gateway -> PrimaryService : enqueue unpublish requests
  PrimaryService -> AsyncBus : send unpublish work
  AsyncBus -> NotificationHub : target-completed or target-failed updates
  NotificationHub --> Store : refresh the same post-builder and post lists
  Store --> Screen : clear published state or surface failures
end
""",
    ),
    Feature(
        slug="meai-user-fetches-ai-recommendation-draft-posts",
        title="User Opens An AI Recommendation Draft Post And Follows The Retrieval Pipeline",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/ai-recommendation/:correlationId",),
        frontend_classes=("AI Recommendation Screen", "Recommendation Query Client", "Recommendation Query Cache"),
        backend_classes=("AI Recommendation Controller", "Draft Post Recommendation Flow", "RAG Query Orchestrator"),
        integrations=("Feed Analytics gRPC", "RabbitMQ Or gRPC To RAG"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : open an AI recommendation by correlation id
Screen -> Hook : start recommendation status query
Hook -> Gateway : GET /api/Ai/recommendations/draft-post/{correlationId}
Gateway -> PrimaryService : load recommendation status and generated payload
group If the generation is still being prepared in the backend
  PrimaryService -> SupportingService : ensure RAG is ready before retrieving references
  SupportingService -> AsyncBus : wait-ready or query request toward RAG
  AsyncBus --> SupportingService : RAG ready or retrieval data returned
  SupportingService -> DomainLayer : merge prompt rewrite, knowledge hits and account context
  DomainLayer -> SupportingService : optionally pull analytics or profile context from Feed and User services
  SupportingService --> DomainLayer : supporting context returned
  DomainLayer --> PrimaryService : draft post recommendation result
end
alt correlation id is unknown or still incomplete
  PrimaryService --> Gateway : status payload with pending or failure information
  Gateway --> Hook : unresolved response
  Hook -> Store : keep query state in loading or error mode
  Store --> Screen : show loading, retry or diagnostic state
else draft recommendation is available
  PrimaryService --> Gateway : generated draft content and references
  Gateway --> Hook : final recommendation payload
  Hook -> Store : cache the result by correlation id
  Store --> Screen : render the returned draft content for the user to inspect
end
""",
    ),
    Feature(
        slug="meai-user-manages-profile-avatar-and-password",
        title="User Updates Profile Fields, Avatar And Password",
        actor="User",
        repos=("MeAI-FE", "MeAI-Social-Platform"),
        routes=("/user/user-settings",),
        frontend_classes=("User Settings Screen", "Profile Query And Mutation Layer", "Current User Store"),
        backend_classes=("User Profile Controller", "User Auth Controller", "Profile Update Flow"),
        integrations=("S3 Avatar Storage", "User Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open account settings
Screen -> Hook : fetch current profile data
Hook -> Gateway : GET /api/User/auth/me
Gateway -> PrimaryService : read authenticated profile
PrimaryService -> DomainLayer : load user profile snapshot
DomainLayer --> PrimaryService : current profile data
PrimaryService --> Gateway : profile response
Gateway --> Hook : profile payload
Hook -> Store : hydrate the form with original values
Store --> Screen : render editable profile state
group Update standard profile fields
  User -> Screen : change name, phone, address or birthday
  Screen -> Hook : submit only changed values
  Hook -> Gateway : PUT or PATCH edit-profile endpoint
  Gateway -> PrimaryService : validate and persist profile changes
  alt validation fails
    PrimaryService --> Gateway : validation errors
    Gateway --> Hook : failure
    Hook --> Screen : highlight invalid fields
  else profile saved
    PrimaryService -> DomainLayer : update profile row
    DomainLayer --> PrimaryService : profile updated
    PrimaryService --> Gateway : success
    Gateway --> Hook : mutation success
    Hook -> Store : refetch current user snapshot
    Store --> Screen : show saved state and updated header identity
  end
end
group Upload avatar
  User -> Screen : choose a new avatar image
  Screen -> Hook : submit multipart avatar upload
  Hook -> Gateway : PUT /api/User/profile/avatar
  Gateway -> PrimaryService : validate media type and size
  PrimaryService -> SupportingService : store avatar object and update user profile
  SupportingService --> PrimaryService : avatar url saved
  PrimaryService --> Gateway : upload success
  Gateway --> Hook : avatar updated
  Hook -> Store : refetch current user snapshot
  Store --> Screen : replace the avatar in settings and global navigation
end
group Change password
  User -> Screen : submit current password and a new password
  Screen -> Hook : call change-password mutation
  Hook -> Gateway : POST /api/User/auth/change-password
  Gateway -> SupportingService : verify old password and write new hash
  alt current password invalid or new password weak
    SupportingService --> Gateway : problem details
    Gateway --> Hook : failure
    Hook --> Screen : show change-password error
  else password changed
    SupportingService --> Gateway : success
    Gateway --> Hook : success
    Hook --> Screen : clear password inputs and show confirmation
  end
end
""",
    ),
    Feature(
        slug="meai-user-purchases-a-subscription-plan",
        title="User Purchases Or Schedules A Subscription Plan Change",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/checkout/:planId", "/user/plans", "/user/billing-history"),
        frontend_classes=("Plan Selection Screen", "Stripe Checkout Screen", "Current User Store"),
        backend_classes=("User Subscription Controller", "Stripe Purchase Flow", "Entitlement Update Flow"),
        integrations=("Stripe Payment Intent", "Subscription And Transaction Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : choose a recurring plan
Screen -> Hook : open the dedicated checkout route
Hook -> Gateway : GET public plans and current user subscriptions
Gateway -> PrimaryService : load available plans and current entitlements
PrimaryService -> DomainLayer : compare active plan, scheduled plan and requested plan
alt requested plan is already active or already scheduled
  DomainLayer --> PrimaryService : reject duplicate purchase
  PrimaryService --> Gateway : business error
  Gateway --> Hook : checkout cannot proceed
  Hook --> Screen : show explanation and send user back to Plans
else purchase can proceed
  Hook -> Gateway : POST create subscription purchase
  Gateway -> PrimaryService : create Stripe purchase session or immediate plan switch
  PrimaryService -> SupportingService : compute credits, next billing date and required payment
  alt no payment is required
    SupportingService --> PrimaryService : immediate activation or scheduled change created
    PrimaryService --> Gateway : completed-without-payment payload
    Gateway --> Hook : purchase completed
    Hook -> Store : refetch current user entitlements
    Store --> Screen : route user to dashboard or plans summary
  else payment is required
    SupportingService --> PrimaryService : Stripe client secret and transaction metadata
    PrimaryService --> Gateway : checkout payload
    Gateway --> Hook : render Stripe payment form
  end
end
group Frontend confirms Stripe payment
  User -> Screen : complete card payment in Stripe form
  Screen -> Hook : confirm payment
  Hook -> Gateway : POST confirm purchase or Stripe webhook-driven completion
  Gateway -> PrimaryService : finalize subscription and transaction
  PrimaryService -> DomainLayer : activate plan or schedule the next renewal change
  DomainLayer --> PrimaryService : entitlements updated
  PrimaryService --> Gateway : final subscription result
  Gateway --> Hook : confirmation success
  Hook -> Store : refetch current user session and entitlements
  Store --> Screen : expose updated plan state and billing-history access
end
""",
    ),
    Feature(
        slug="meai-user-purchases-a-coin-package",
        title="User Purchases A Coin Package And The Balance Refreshes After Confirmation",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/checkout/coin-package", "/user/plans"),
        frontend_classes=("Coin Package Checkout Screen", "Stripe Payment Form", "Current User Store"),
        backend_classes=("Coin Package Billing Controller", "Coin Checkout Flow", "Balance Update Flow"),
        integrations=("Stripe Payment Intent", "Transaction And Coin Balance Store"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : choose a coin package from the plans area
Screen -> Hook : open coin-package checkout with client secret and payment metadata
Hook -> Gateway : POST coin-package checkout
Gateway -> PrimaryService : create one-time payment intent
PrimaryService -> SupportingService : persist pending transaction and Stripe identifiers
SupportingService --> PrimaryService : checkout state ready
PrimaryService --> Gateway : client secret and transaction id
Gateway --> Hook : payment form can render
Hook --> Screen : display package size, price and payment widget
group Successful payment confirmation
  User -> Screen : submit the Stripe payment form
  Screen -> Hook : wait for Stripe success then resolve checkout
  Hook -> Gateway : POST resolve coin-package checkout
  Gateway -> PrimaryService : confirm payment intent and transaction
  alt payment could not be confirmed
    PrimaryService --> Gateway : failure
    Gateway --> Hook : resolution error
    Hook --> Screen : show payment confirmation failure
  else checkout resolved
    PrimaryService -> DomainLayer : credit coins to the user balance
    DomainLayer --> PrimaryService : new balance and added-coin amount
    PrimaryService --> Gateway : checkout resolved
    Gateway --> Hook : success with new balance
    Hook -> Store : refetch current user balance
    Store --> Screen : return user to plans with the updated coin total
  end
end
""",
    ),
    Feature(
        slug="meai-user-dashboard-and-product-analytics",
        title="User Reviews Dashboard Summaries, Product Lists And Analytics Snapshots",
        actor="User",
        repos=("MeAI-FE",),
        routes=("/user/dashboard", "/user/product", "/user/product-detail"),
        frontend_classes=("Dashboard Screen", "Product List Screen", "Analytics Query Layer"),
        backend_classes=("AI Post Query Controller", "Dashboard Summary Flow", "Feed Analytics Bridge"),
        integrations=("Feed Analytics gRPC", "AI Post Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open the dashboard or product area
Screen -> Hook : request post lists, dashboard summaries and analytics slices
Hook -> Gateway : GET post summaries and list endpoints
Gateway -> PrimaryService : load AI-owned posts for the user or workspace
PrimaryService -> DomainLayer : fetch draft, published and failed post groups
opt Dashboard requires analytics enrichment
  DomainLayer -> SupportingService : ask Feed service for analytics snapshots over gRPC
  SupportingService --> DomainLayer : engagement metrics and time-series summaries
end
DomainLayer --> PrimaryService : merged post and analytics view
PrimaryService --> Gateway : response payloads
Gateway --> Hook : dashboard and product data
Hook -> Store : cache post groups, charts and summary tiles
Store --> Screen : render published, draft and failure states
opt User refreshes or changes filters
  Screen -> Hook : rerun the affected list or summary query
  Hook -> Gateway : GET filtered data
  Gateway -> PrimaryService : execute filtered read model
  PrimaryService --> Gateway : refreshed payload
  Gateway --> Hook : new slice
  Hook -> Store : update cached cards and charts
  Store --> Screen : keep the dashboard synchronized without leaving the page
end
""",
    ),
    Feature(
        slug="meai-admin-manages-users-and-subscriptions",
        title="Admin Manages Users And Their Subscription Status",
        actor="Admin",
        repos=("MeAI-FE",),
        routes=("/admin/admin-users",),
        frontend_classes=("Admin Users Screen", "Admin Query Layer", "User Edit Dialog"),
        backend_classes=("Admin User Controller", "Admin User Subscription Controller", "Admin User Management Flow"),
        integrations=("User Database", "Subscription Database"),
        has_async_delivery=False,
        sequence_body="""
Admin -> Screen : open the user administration page
Screen -> Hook : load users, subscription plans and user-subscription rows
Hook -> Gateway : GET admin users, subscriptions and user subscriptions
Gateway -> PrimaryService : fetch user inventory
PrimaryService -> SupportingService : read available plans and each user's active or scheduled subscription
SupportingService --> PrimaryService : user and subscription datasets
PrimaryService --> Gateway : combined admin payloads
Gateway --> Hook : administration data
Hook -> Store : cache rows for sorting, filtering and dialogs
Store --> Screen : render the full admin table
group Create, update or activate a user
  Admin -> Screen : submit a create or update dialog
  Screen -> Hook : call the relevant admin mutation
  Hook -> Gateway : POST, PUT or activate admin-user endpoint
  Gateway -> PrimaryService : validate admin command
  alt validation fails
    PrimaryService --> Gateway : problem details
    Gateway --> Hook : failure
    Hook --> Screen : show field-level or toast error
  else command succeeds
    PrimaryService -> DomainLayer : persist user changes
    DomainLayer --> PrimaryService : updated user state
    PrimaryService --> Gateway : success
    Gateway --> Hook : mutation success
    Hook -> Store : invalidate admin user queries
    Store --> Screen : refresh the table and subscription badges
  end
end
group Change one user's subscription status
  Admin -> Screen : choose a status update or manual reassignment
  Screen -> Hook : call admin user-subscription mutation
  Hook -> Gateway : PATCH user-subscription endpoint
  Gateway -> SupportingService : update subscription state and effective dates
  SupportingService --> Gateway : updated entitlement
  Gateway --> Hook : success
  Hook -> Store : refetch user-subscription data
  Store --> Screen : show the new entitlement state in the same grid
end
""",
    ),
    Feature(
        slug="meai-admin-manages-transactions-config-storage-and-usage",
        title="Admin Reviews Transactions, System Config And Storage Usage",
        actor="Admin",
        repos=("MeAI-FE",),
        routes=("/admin/admin-transactions", "/admin/admin-config", "/admin/admin-resource", "/admin/dashboard"),
        frontend_classes=("Admin Dashboard Screen", "Admin Transactions Screen", "Admin Storage And Config Screen"),
        backend_classes=("Admin Transaction Controller", "Admin Config Controller", "Admin Storage Controller"),
        integrations=("Transaction Database", "Storage Usage Store", "AI Spending Read Model"),
        has_async_delivery=False,
        sequence_body="""
Admin -> Screen : open an admin dashboard, transaction or storage/config page
Screen -> Hook : request the required datasets
Hook -> Gateway : GET admin transactions, storage usage, plan settings, config and spending summaries
Gateway -> PrimaryService : route each request to the proper admin controller
PrimaryService -> DomainLayer : read transaction rows, configuration values and quota aggregates
opt Admin dashboard requests AI spending history too
  DomainLayer -> SupportingService : read AI spending and usage snapshots from the AI service
  SupportingService --> DomainLayer : spending overview and history
end
DomainLayer --> PrimaryService : combined read models
PrimaryService --> Gateway : admin payloads
Gateway --> Hook : responses for tables, charts and forms
Hook -> Store : cache admin read models
Store --> Screen : render transaction review, system config and storage analysis
group Admin updates config or storage limits
  Admin -> Screen : submit a config or quota change
  Screen -> Hook : call update mutation
  Hook -> Gateway : PUT or PATCH admin config or storage endpoint
  Gateway -> PrimaryService : validate admin command
  PrimaryService -> DomainLayer : persist new limit, free tier or cleanup settings
  opt Admin runs cleanup or reconcile
    DomainLayer -> SupportingService : execute maintenance operation
    SupportingService --> DomainLayer : maintenance summary
  end
  DomainLayer --> PrimaryService : updated configuration
  PrimaryService --> Gateway : success response
  Gateway --> Hook : mutation success
  Hook -> Store : invalidate config and usage queries
  Store --> Screen : show refreshed values and maintenance outcomes
end
""",
    ),
    Feature(
        slug="meai-admin-reviews-and-resolves-feed-reports",
        title="Admin Reviews Reported Feed Content And Resolves The Decision",
        actor="Admin",
        repos=("MeAI-FE",),
        routes=("/admin/admin-report",),
        frontend_classes=("Admin Report Screen", "Report Review Query Layer", "Report Decision Dialog"),
        backend_classes=("Feed Report Controller", "Report Review Flow", "Feed Moderation Repository"),
        integrations=("Feed Database", "Notification Delivery Pipeline"),
        has_async_delivery=True,
        sequence_body="""
Admin -> Screen : open the moderation report page
Screen -> Hook : load admin report list and preview data
Hook -> Gateway : GET admin reports and report preview endpoints
Gateway -> PrimaryService : fetch moderation data from the Feed service
PrimaryService -> DomainLayer : load report rows, target content and reporter context
DomainLayer --> PrimaryService : moderation preview model
PrimaryService --> Gateway : report payloads
Gateway --> Hook : report list and preview
Hook -> Store : cache report queue and selected report details
Store --> Screen : render the moderation workspace
group Admin reviews one report
  Admin -> Screen : accept, reject or otherwise resolve the report
  Screen -> Hook : submit report-review mutation
  Hook -> Gateway : POST or PATCH review endpoint
  Gateway -> PrimaryService : validate admin decision
  PrimaryService -> DomainLayer : update report state and apply moderation outcome
  alt content must be hidden or removed
    DomainLayer -> SupportingService : update feed visibility and final moderation state
    SupportingService --> DomainLayer : moderated content saved
  else report dismissed
    DomainLayer --> PrimaryService : report closed without content change
  end
  opt resolution emits downstream notifications
    DomainLayer -> AsyncBus : request moderation-related notification delivery
    AsyncBus -> NotificationHub : notification accepted
  end
  PrimaryService --> Gateway : review result
  Gateway --> Hook : success
  Hook -> Store : invalidate report list and selected preview
  Store --> Screen : remove or update the reviewed item in the queue
end
""",
    ),
    Feature(
        slug="social-platform-authentication-journey",
        title="Community User Signs In, Signs Up Or Uses Forgot Password In The Social Platform",
        actor="User",
        repos=("MeAI-Social-Platform",),
        routes=("/auth/signin", "/auth/signup", "/auth/forgot-password"),
        frontend_classes=("Community Auth Screens", "Axios Fetcher With Refresh", "Community Session Store"),
        backend_classes=("User Auth Controller", "Refresh Token Flow", "Registration And Reset Flow"),
        integrations=("Redis Refresh Store", "Verification Mail Sender"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : choose sign in, sign up or forgot-password on the community site
group Sign in path
  Screen -> Hook : submit credentials
  Hook -> Gateway : POST /api/User/auth/login
  Gateway -> PrimaryService : authenticate community user
  alt login fails
    PrimaryService --> Gateway : unauthorized response
    Gateway --> Hook : error
    Hook --> Screen : show sign-in failure
  else login succeeds
    PrimaryService --> Gateway : cookies and authenticated user
    Gateway --> Hook : success
    Hook -> Store : persist community session
    Store --> Screen : route user to the feed
  end
end
group Sign up or forgot-password path
  User -> Screen : request verification code and submit the follow-up form
  Screen -> Hook : call the same user-auth flows used by the main product
  Hook -> Gateway : verification and register or reset endpoints
  Gateway -> PrimaryService : execute registration or reset workflow
  PrimaryService -> SupportingService : deliver email code and persist result
  alt request fails
    SupportingService --> PrimaryService : failure
    PrimaryService --> Gateway : error contract
    Gateway --> Hook : failure
    Hook --> Screen : show the issue
  else flow succeeds
    SupportingService --> PrimaryService : success
    PrimaryService --> Gateway : final success payload
    Gateway --> Hook : success
    Hook -> Store : update auth state when appropriate
    Store --> Screen : route the user back to sign in or into the feed
  end
end
opt A protected request later receives 401
  Hook -> PrimaryService : use the shared refresh-token interceptor
  PrimaryService -> SupportingService : validate refresh token
  SupportingService --> PrimaryService : refreshed access or logout instruction
  PrimaryService --> Hook : session result
  Hook --> Screen : continue the action or redirect to sign-in
end
""",
    ),
    Feature(
        slug="social-platform-feed-browsing-and-like-toggle",
        title="Community User Browses The Feed And Toggles Likes With Optimistic UI",
        actor="User",
        repos=("MeAI-Social-Platform",),
        routes=("/", "/:username"),
        frontend_classes=("Feed Screen", "Feed Query Hooks", "React Query Cache"),
        backend_classes=("Feed Post Controller", "Feed Query Flow", "Like Toggle Flow"),
        integrations=("Feed Database", "Public Profile Read Model"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open the home feed or a user profile
Screen -> Hook : start infinite feed or profile queries
Hook -> Gateway : GET feed posts, profile and profile-posts endpoints
Gateway -> PrimaryService : load the requested slice
PrimaryService -> DomainLayer : read posts, owners and cursor state
DomainLayer --> PrimaryService : post list and cursor
PrimaryService --> Gateway : paged payload
Gateway --> Hook : page response
Hook -> Store : cache pages for infinite scrolling
Store --> Screen : render posts and next-page cursor
opt User scrolls further
  Screen -> Hook : fetch next page
  Hook -> Gateway : GET next cursor page
  Gateway -> PrimaryService : execute feed cursor query
  PrimaryService --> Gateway : next page
  Gateway --> Hook : append page
  Hook -> Store : extend cached feed pages
  Store --> Screen : continue the scroll without resetting prior items
end
group User toggles a post like
  User -> Screen : tap like or unlike
  Screen -> Store : optimistically flip like state and count
  Store -> Hook : fire like or unlike mutation in the background
  Hook -> Gateway : POST or DELETE /api/Feed/posts/{id}/like
  Gateway -> PrimaryService : update like state
  alt backend rejects the mutation
    PrimaryService --> Gateway : failure
    Gateway --> Hook : error
    Hook -> Store : roll back the optimistic state
    Store --> Screen : restore the previous count
  else backend accepts the mutation
    PrimaryService -> DomainLayer : persist like edge
    DomainLayer --> PrimaryService : final like state
    PrimaryService --> Gateway : success
    Gateway --> Hook : authoritative count and flag
    Hook -> Store : reconcile optimistic cache with the final values
    Store --> Screen : keep the liked state stable
  end
end
""",
    ),
    Feature(
        slug="social-platform-post-discussion-reporting-and-moderation-entry",
        title="Community User Opens A Post, Talks In Comments And Reports Problematic Content",
        actor="User",
        repos=("MeAI-Social-Platform",),
        routes=("/:username/post/:postId",),
        frontend_classes=("Post Detail Screen", "Comment And Report Mutations", "React Query Cache"),
        backend_classes=("Feed Post Controller", "Comment Flow", "Report Flow"),
        integrations=("Feed Database", "Notification Delivery Pipeline"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : open a post detail page
Screen -> Hook : load post detail and first page of comments
Hook -> Gateway : GET post detail and comments endpoints
Gateway -> PrimaryService : fetch post and discussion state
PrimaryService -> DomainLayer : read post, author and comment cursor
DomainLayer --> PrimaryService : detail payload
PrimaryService --> Gateway : post and comments
Gateway --> Hook : detail response
Hook -> Store : cache detail and comment lists
Store --> Screen : render the post, comment composer and moderation actions
group Create a comment or reply
  User -> Screen : type a comment or a reply
  Screen -> Hook : submit create-comment or create-reply mutation
  Hook -> Gateway : POST comment endpoint
  Gateway -> PrimaryService : validate author and target post or comment
  PrimaryService -> DomainLayer : persist the new discussion item
  opt New interaction should notify the owner
    DomainLayer -> AsyncBus : request notification delivery
    AsyncBus -> NotificationHub : notification queued for realtime delivery
  end
  DomainLayer --> PrimaryService : comment created
  PrimaryService --> Gateway : success
  Gateway --> Hook : comment payload
  Hook -> Store : invalidate post detail and comment queries
  Store --> Screen : refresh counts and show the new discussion item
end
group Report or delete content
  User -> Screen : report a post or comment, or delete own content
  Screen -> Hook : submit report or delete mutation
  Hook -> Gateway : POST /reports or DELETE comment or post endpoint
  Gateway -> PrimaryService : validate ownership or report target
  PrimaryService -> DomainLayer : persist the report row or remove owned content
  DomainLayer --> PrimaryService : mutation result
  PrimaryService --> Gateway : success
  Gateway --> Hook : final response
  Hook -> Store : invalidate the affected queries
  Store --> Screen : update the discussion view and moderation state
end
""",
    ),
    Feature(
        slug="social-platform-user-follows-and-explores-the-social-graph",
        title="Community User Follows People And Explores Followers, Following And Suggestions",
        actor="User",
        repos=("MeAI-Social-Platform",),
        routes=("/:username", "/followers", "/activity"),
        frontend_classes=("Profile And Follower Screens", "Follow Query Hooks", "React Query Cache"),
        backend_classes=("Feed Follow Controller", "Follow Graph Flow", "Suggestion Query Flow"),
        integrations=("Follow Graph Database", "Notification Delivery Pipeline"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : open a profile, follower list or activity area
Screen -> Hook : load follow suggestions, followers or following pages
Hook -> Gateway : GET follow suggestions, followers or following endpoints
Gateway -> PrimaryService : execute the requested social-graph query
PrimaryService -> DomainLayer : read suggestion or follow-edge pages
DomainLayer --> PrimaryService : graph slice and cursor
PrimaryService --> Gateway : paged result
Gateway --> Hook : follow payload
Hook -> Store : cache graph data
Store --> Screen : render suggestions and relationship lists
group Follow or unfollow another user
  User -> Screen : click Follow or Unfollow
  Screen -> Hook : submit follow mutation
  Hook -> Gateway : POST or DELETE /api/Feed/follow/{userId}
  Gateway -> PrimaryService : validate relationship change
  PrimaryService -> DomainLayer : create or remove follow edge
  opt Following someone should notify them
    DomainLayer -> AsyncBus : request follow notification
    AsyncBus -> NotificationHub : notification queued
  end
  DomainLayer --> PrimaryService : updated follow relationship
  PrimaryService --> Gateway : success
  Gateway --> Hook : mutation result
  Hook -> Store : invalidate suggestion and follower queries
  Store --> Screen : refresh counts, buttons and list membership
end
opt User loads more followers or following
  Screen -> Hook : fetch next cursor page
  Hook -> Gateway : GET next follower page
  Gateway -> PrimaryService : continue graph query
  PrimaryService --> Gateway : next page
  Gateway --> Hook : next slice
  Hook -> Store : append to the cached list
  Store --> Screen : keep the infinite list continuous
end
""",
    ),
    Feature(
        slug="social-platform-profile-edit-avatar-and-password",
        title="Community User Updates Public Profile Details, Avatar And Password",
        actor="User",
        repos=("MeAI-Social-Platform",),
        routes=("/activity",),
        frontend_classes=("Profile Settings Drawer", "Profile API Layer", "Community Session Store"),
        backend_classes=("User Profile Controller", "User Auth Controller", "Profile Update Flow"),
        integrations=("S3 Avatar Storage", "User Database"),
        has_async_delivery=False,
        sequence_body="""
User -> Screen : open the editable account settings area in the social platform
Screen -> Hook : load the current authenticated profile
Hook -> Gateway : GET /api/User/auth/me
Gateway -> PrimaryService : read current user profile
PrimaryService --> Gateway : profile payload
Gateway --> Hook : me response
Hook -> Store : hydrate profile form values
Store --> Screen : render current public profile state
group Update profile fields
  User -> Screen : change bio-facing fields
  Screen -> Hook : submit profile update
  Hook -> Gateway : PUT edit-profile endpoint
  Gateway -> PrimaryService : validate and persist the change
  PrimaryService --> Gateway : update result
  Gateway --> Hook : success or failure
  Hook -> Store : refresh current profile on success
  Store --> Screen : show updated public identity
end
group Upload avatar or change password
  User -> Screen : submit avatar file or password form
  Screen -> Hook : call avatar or change-password endpoint
  Hook -> Gateway : multipart avatar request or password mutation
  Gateway -> SupportingService : store avatar object or validate old password
  alt request fails
    SupportingService --> Gateway : error response
    Gateway --> Hook : failure
    Hook --> Screen : show the validation or upload error
  else request succeeds
    SupportingService --> Gateway : success
    Gateway --> Hook : success
    Hook -> Store : refresh current user
    Store --> Screen : show updated avatar or cleared password form
  end
end
""",
    ),
    Feature(
        slug="social-platform-realtime-notifications",
        title="Community User Receives Realtime Notifications And Marks Them As Read",
        actor="User",
        repos=("MeAI-Social-Platform",),
        routes=("/activity",),
        frontend_classes=("Notification Provider", "Notification Bell And Activity UI", "SignalR Connection Helper"),
        backend_classes=("Notification Controller", "Notification Hub", "Notification Persistence Flow"),
        integrations=("SignalR Transport", "Notification Database"),
        has_async_delivery=True,
        sequence_body="""
User -> Screen : open the area that contains notifications
Screen -> Hook : fetch initial notification list
Hook -> Gateway : GET /api/Notification/notifications
Gateway -> PrimaryService : read stored notifications for the current user
PrimaryService -> DomainLayer : query notifications and unread state
DomainLayer --> PrimaryService : notification list
PrimaryService --> Gateway : list response
Gateway --> Hook : notifications payload
Hook -> Store : cache notifications and unread count
Store --> Screen : render the initial list
group Frontend opens realtime delivery
  Hook -> NotificationHub : start SignalR connection
  NotificationHub --> Hook : connected
  Hook -> Store : mark realtime channel as connected
  Store --> Screen : show live notification state
end
group A new notification arrives later
  PrimaryService -> NotificationHub : publish NotificationReceived event
  NotificationHub --> Hook : deliver notification payload
  Hook -> Store : upsert the notification at the top of the list
  Store --> Screen : update unread badge and visible activity stream without full page reload
  opt Reconnect happens after network loss
    NotificationHub --> Hook : reconnected event
    Hook -> Gateway : refetch notifications
    Gateway -> PrimaryService : read authoritative list
    PrimaryService --> Gateway : refreshed notifications
    Gateway --> Hook : latest payload
    Hook -> Store : resynchronize with server truth
    Store --> Screen : keep the badge and list accurate after reconnect
  end
end
group User marks notifications as read
  User -> Screen : mark one item or all items as read
  Screen -> Hook : send mark-read mutation
  Hook -> Gateway : PATCH notification read endpoint
  Gateway -> PrimaryService : persist read timestamp
  PrimaryService --> Gateway : success
  Gateway --> Hook : mutation success
  Hook -> Store : update local read flags immediately
  Store --> Screen : drop unread counts and update styling
end
""",
    ),
)


EXCLUDED_ITEMS: tuple[tuple[str, str, str], ...] = (
    ("MeAI-FE", "/guest/* and /errors/*", "Informational or static pages with no backend workflow."),
    ("MeAI-FE", "/ai-content-automation", "Current screen is a UI placeholder with no active API flow."),
    ("MeAI-FE", "/checkout/stripe-result", "Completion state is folded into the checkout diagrams above."),
)


def tracker_content() -> str:
    lines = [
        "# Frontend Feature Diagram Tracker",
        "",
        "This tracker maps every frontend feature diagrammed in `plantuml/features/` to the frontend routes that exposed it.",
        "",
        "| Status | Feature | Repo | Frontend Routes | Diagram |",
        "|---|---|---|---|---|",
    ]
    for feature in FEATURES:
        repos = ", ".join(feature.repos)
        routes = "<br>".join(feature.routes)
        diagram = f"[{feature.slug}.puml](/home/vinhdo/Documents/GitHub/MeAI-BE/plantuml/features/{feature.slug}.puml:1)"
        lines.append(f"| Drawn | {feature.title} | {repos} | {routes} | {diagram} |")

    lines.extend(
        [
            "",
            "## Excluded UI Surfaces",
            "",
            "| Repo | Route Or Area | Reason |",
            "|---|---|---|",
        ]
    )
    for repo, area, reason in EXCLUDED_ITEMS:
        lines.append(f"| {repo} | {area} | {reason} |")

    return "\n".join(lines) + "\n"


def readme_content() -> str:
    lines = [
        "# PlantUML Output",
        "",
        "This folder contains end-to-end frontend feature diagrams, not raw endpoint inventories.",
        "",
        "## Structure",
        "",
        "- `features/`: one `.puml` file per frontend feature.",
        "- Every file contains two PlantUML blocks: a class view and a detailed sequence flow.",
        "- Sequence flows always begin with `User` or `Admin` and run through frontend, gateway, backend services, async delivery and frontend refresh paths when the feature uses them.",
        "",
        "## Coverage Rules Used",
        "",
        "- Focus only on real frontend features found in `MeAI-FE` and `MeAI-Social-Platform`.",
        "- Keep the language human-readable instead of endpoint-slug style naming.",
        "- Keep each sequence flow long and continuous so the return path back to frontend is visible.",
        "- Do not use PlantUML `note` blocks.",
        "",
        "## Diagram Inventory",
        "",
        f"- Frontend feature diagrams: `{len(FEATURES)}`",
        f"- Excluded UI-only surfaces: `{len(EXCLUDED_ITEMS)}`",
        f"- Tracker: [frontend-feature-diagram-tracker.md](/home/vinhdo/Documents/GitHub/MeAI-BE/plans/frontend-feature-diagram-tracker.md:1)",
        "",
    ]
    return "\n".join(lines)


def reset_output_dirs() -> None:
    for path in (
        PLANTUML_ROOT / "http",
        PLANTUML_ROOT / "grpc",
        PLANTUML_ROOT / "rabbitmq",
        PLANTUML_ROOT / "fr",
        PLANTUML_ROOT / "fr-detail",
        FEATURES_DIR,
    ):
        if path.exists():
            shutil.rmtree(path)
    FEATURES_DIR.mkdir(parents=True, exist_ok=True)


def write_outputs() -> None:
    reset_output_dirs()
    for feature in FEATURES:
        (FEATURES_DIR / f"{feature.slug}.puml").write_text(feature_file(feature), encoding="utf-8")

    TRACKER_PATH.write_text(tracker_content(), encoding="utf-8")
    README_PATH.write_text(readme_content(), encoding="utf-8")

    old_tracker = REPO_ROOT / "plans" / "owned-surface-diagram-tracker.md"
    if old_tracker.exists():
        old_tracker.unlink()


def main() -> None:
    write_outputs()
    print(f"Generated {len(FEATURES)} frontend feature diagrams in {FEATURES_DIR}")
    print(f"Tracker written to {TRACKER_PATH}")


if __name__ == "__main__":
    main()
