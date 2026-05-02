# AGENTS.md MeAI

## Project overview
- .NET 10 microservices template with Clean Architecture per service.
- Services: User, Ai, Feed, Notification, API Gateway; shared code in SharedLibrary.
- Infra: Docker Compose for local, Terraform for AWS (ECS/EC2 or EKS), optional CloudFront/Cloudflare.

## Repository layout (top-level)
```
Backend/
  Compose/              # docker compose stacks, nginx, postgres init
  Kubernetes/           # manifests and kind cluster config
  Microservices/
    Microservices.sln
    Ai.Microservice/
    Feed.Microservice/
    Notification.Microservice/
    User.Microservice/
    ApiGateway/
    SharedLibrary/
Terraform/              # infra root (modules, scripts, main_*.tf)
Terraform-vars/         # sanitized tfvars templates (checked in)
terraform-var/          # private tfvars (gitignored)
.github/workflows/
```

## Microservice layout (per service)
```
Backend/Microservices/<Service>.Microservice/
  dockerfile
  src/
    Domain/
    Application/
    Infrastructure/
    WebApi/
  test/
```

## Typical layer folders (conventions)
- Domain: `Entities/`, `Repositories/` (interfaces).
- Application: feature-first folders (e.g., `Users/`, `Workspaces/`) with `Commands/`, `Queries/`, `Models/`, `Validators/`.
- Infrastructure: `Context/`, `Repositories/`, `Migrations/`, `Configs/`, `Common/`, `Logic/` (service logic folders like `Logic/Services/`, `Logic/Consumers/`, `Logic/Sagas/`, `Logic/Payments/`, `Logic/Security/`, `Logic/Storage/`, `Logic/Threads/`, `Logic/TikTok/`, `Logic/Seeding/`), plus assets like `EmailTemplates/`.
- WebApi: `Controllers/`, `Setups/`, `Middleware/`, `Mapping/`, `Properties/`.

## Naming conventions
- Folder and file names: PascalCase for C# (`AuthController.cs`, `CreateUserCommand.cs`).
- Projects/services: service folders end with `.Microservice`; gateway is `ApiGateway`; shared code is `SharedLibrary`.
- Namespaces: file-scoped and mirror folders (`Domain.*`, `Application.<Feature>.<Commands|Queries|Models|Validators>`, `Infrastructure.*`, `WebApi.*`).
- CQRS types: `*Command`/`*Query`, handlers `*Handler`, validators `*Validator`.
- DTOs/models: `*Request`, `*Response`, or `*Model` (see `AuthenticationModel`, `AdminUserResponse`).
- Setup extensions: `*Setup` classes in `WebApi/Setups` (e.g., `CorsSetup`, `DatabaseSetup`).
- Dockerfiles: services use `dockerfile` (lowercase); CI also accepts `Dockerfile`/`DockerFile`.

## Architecture & design patterns
- Clean Architecture per service: `Domain` -> `Application` -> `Infrastructure` -> `WebApi`.
- CQRS via MediatR; shared abstractions in `Backend/Microservices/SharedLibrary/Abstractions/Messaging`.
- Validation via FluentValidation; mapping via AutoMapper.
- Messaging via MassTransit/RabbitMQ; Ai service includes saga state machines.
- SharedLibrary holds auth, configs, middleware, contracts, and response models.
- Assembly scanning uses `AssemblyReference.cs` in each layer.
- Cross-service resource access: use gRPC for sync calls (e.g., presigned URL fetch before caption generation) and RabbitMQ for async workflows.

## Architecture rules enforced by tests
- Domain must not depend on Application/Infrastructure/WebApi.
- Application must not depend on Infrastructure/WebApi.
- Infrastructure must not depend on WebApi.
- Handlers are expected to depend on Domain.
- Tests live at `Backend/Microservices/*/test/ArchitectureTest.cs`.
- There is an intended controller-to-MediatR dependency assertion in the architecture tests; verify the current test assembly target before relying on it as strictly enforced coverage.

## Coding/build conventions
- Target framework: net10.0; `Nullable` and `ImplicitUsings` enabled.
- Build outputs are redirected to `Build/bin` and `Build/obj` per project.
- EF Core tooling expects custom `MSBuildProjectExtensionsPath`; targets are copied from `SharedLibrary/Configs/Infrastructure.csproj.EntityFrameworkCore.targets`.
- Generated folders under `Build/` are not source of truth; do not hand-edit.
- Facebook/Instagram OAuth + Graph API logic must live in `Infrastructure/Logic/*` services with Application abstractions (mirror the TikTok/Threads flow); commands should only orchestrate via those interfaces.

## Local dev quick refs
- Build/test: `dotnet build` / `dotnet test` from repo root.
- Run a service: `dotnet run --project Backend/Microservices/User.Microservice/src/WebApi/WebApi.csproj`.
- Compose (dev): `docker compose -f Backend/Compose/docker-compose.yml up -d --build`.
- Default ports (dev/prod compose): API Gateway 8080 (host 2406), User HTTP 5002 + gRPC 5004, Ai HTTP 5001 + gRPC 5005, Notification HTTP 5006, Feed HTTP 5007 + gRPC 5008, Postgres 5432, Redis 6379, RabbitMQ 5672/15672, n8n 5678 (via nginx in dev compose).
- Service gRPC ports are internal unless explicitly published by compose/k8s/Terraform.

## Current service map
- User owns auth, profile, resources/S3 presigned URLs, subscriptions/billing, social media OAuth/account metadata, workspaces, admin users/subscriptions/transactions/config, and gRPC services consumed by Ai/Feed.
- Ai owns chats/chat sessions, AI generation, Kie/Veo callbacks, post builders/posts, async publish/unpublish/update consumers, and gRPC calls to User/Feed.
- Feed owns public feed, profiles, posts, comments, follows, reports, analytics snapshots, demo feed seed data, and gRPC analytics consumed by Ai.
- Notification owns notification APIs and SignalR hub `/hubs/notifications`; the gateway also exposes `/api/Notification/hubs/notifications` with the `/api/Notification` prefix removed.
- API Gateway dynamically maps `/api/User`, `/api/Ai`, `/api/AiGeneration`, `/api/Notification`, `/api/Feed`, and any extra `Services__{Service}__Host/Port` or `{PREFIX}_MICROSERVICE_HOST/PORT` entries.

## Frontend/API response contract
- Preserve the existing FE-facing response shape and status code for any endpoint you touch. Do not rename, remove, reorder semantics, or wrap top-level JSON fields on existing routes unless the frontend contract is being updated in the same change.
- Success responses typically return `Ok(result)` where `result` is `SharedLibrary.Common.ResponseModel.Result`/`Result<T>`; keep that envelope for existing success flows unless the endpoint already uses a different established contract.
- Business and validation failures should continue to flow through `HandleFailure(result)` so the response stays as `ProblemDetails` with the current `status`, `type`, `detail`, and optional `errors` extension shape.
- Unauthorized responses must keep the current single-message contract already used by that endpoint/service (`MessageResponse` or the existing anonymous `{ message: "Unauthorized" }` shape after JSON serialization). Do not introduce ad hoc wrappers such as `{ success, data }` or `{ error: ... }` on existing endpoints.
- When changing controllers, commands, queries, DTOs, or OpenAPI annotations, inspect the current controller response types and keep the frontend contract backward-compatible by default.

## Subscription/billing invariants
- Subscription plan delete is a soft delete: set plan `IsActive=false`, `IsDeleted=true`, `DeletedAt`, and keep the row readable so existing user subscription history can still display the old plan name/price.
- Deleting an active plan should mark current active user subscriptions for that plan as `non_renewable`; do not hard-delete or expire them immediately. They remain current until `EndDate` and should display `displayStatus: "No recurring"`.
- `CurrentUserSubscriptionResponse` and admin user-subscription responses include `displayStatus`, `isAutoRenewEnabled`, and `autoRenewStatus`; FE should prefer these for labels/state instead of deriving user-facing text from raw `status`.
- Users control their own current subscription auto-renew only with `POST /api/User/subscriptions/current/auto-renew` and body `{ "enabled": true|false }`. Disabling sets the current user subscription to `non_renewable`, keeps access until `EndDate`, cancels local scheduled changes, and sets Stripe `cancel_at_period_end=true` when Stripe recurring state exists. Enabling requires an active, non-deleted plan and a Stripe recurring subscription, then sets Stripe `cancel_at_period_end=false` and returns status `Active`.
- Admin user-subscription APIs live under `/api/User/admin/user-subscriptions` and edit the user's subscription status only, not the subscription plan catalog row.
- Admin user-subscription status updates use `POST /api/User/admin/user-subscriptions/{userSubscriptionId}/status` with body `{ "status": "...", "reason": "..." }`; `reason` is required, may contain HTML for email rendering, and the command publishes a `user.subscription.status_changed` notification plus a best-effort email to the affected user. The in-app notification `message` must stay plain text; preserve raw HTML in payload fields such as `reasonHtml`.
- Admin user-subscription auto-renew updates use `POST /api/User/admin/user-subscriptions/{userSubscriptionId}/auto-renew` with body `{ "enabled": true|false, "reason": "..." }`; `reason` is required, may contain HTML for email rendering, and the command publishes a `user.subscription.auto_renew_changed` notification plus a best-effort email to the affected user.
- User Stripe card APIs live under `/api/User/billing/cards`: `GET` lists saved card payment methods, `POST` creates a Stripe SetupIntent for adding a card through Stripe Elements, and `POST /api/User/billing/cards/{paymentMethodId}/default` switches the default card for the Stripe customer and linked subscriptions. Never accept raw card numbers in the backend.
- Users have nullable `stripe_customer_id`; card APIs create it when starting card setup and backfill it from existing Stripe subscriptions when possible. New subscription purchases should reuse this customer ID instead of creating duplicate Stripe customers.

## API testing & temp files
- Use `curl` for manual API endpoint testing (use `curl.exe` in Windows shells when needed).
- Test through the API Gateway host/port unless you intentionally need direct service access.
- For auth flows, use cookie jar flags (`-c` and `-b`) or bearer tokens explicitly.
- If temporary files are needed for requests (for example JSON payloads), create them only under `<repo-root>/.temp/`.
- In PowerShell, prefer JSON body files over inline `--data-raw` strings because quote handling can corrupt JSON before it reaches `curl.exe`. Write the payload to `.temp/<case>.json` and send it with `curl.exe --data-binary "@.temp/<case>.json" -H "Content-Type: application/json"`.
- When reporting curl test results, include the request method, URL, request body, HTTP status, and response body for each case. Mask passwords, bearer tokens, refresh tokens, API keys, and other secrets.
- Remove temporary test artifacts from `.temp/` after use and do not commit sensitive test data.
- For APIs that require third-party callbacks/webhooks, use `ngrok` to expose the gateway (typically `http://localhost:2406`).
- If `ngrok` is already running, reuse the existing tunnel and fetch its current URL before starting a new tunnel.
- When the `ngrok` URL changes, update callback/redirect env values in `Backend/Compose/docker-compose-production.yml`, then restart production compose to apply it.
- Restart flow for URL updates: `docker compose -f Backend/Compose/docker-compose-production.yml down` then `docker compose -f Backend/Compose/docker-compose-production.yml up -d --build`.
- If production compose is not running yet, start it with `docker compose -f Backend/Compose/docker-compose-production.yml up -d --build`, then run `ngrok` and test.
- After `ngrok` is ready, test endpoints using `curl` against the expected public callback flow and/or local gateway route.
- If this test run started `ngrok`, shut it down after testing; if `ngrok` was already running before the test, keep it running for future tests.

## API Gateway (YARP)
- Runtime config is generated in `Backend/Microservices/ApiGateway/src/Setups/YarpRuntimeSetup.cs`.
- Default routes are generated for `/api/User` and `/api/Ai`.
- When Ai is enabled, the gateway also adds `/api/Gemini` as an alias route to the Ai service.
- Extra services can be added via `Services__{Service}__Host` and `Services__{Service}__Port` (or `{PREFIX}_MICROSERVICE_HOST/PORT`).
- OpenAPI aggregation pulls `/openapi/v1.json` from each service.
- Runtime config is written to `yarp.runtime.json` in the gateway content root.
- Gateway docs UI is configured by `ENABLE_DOCS_UI`; current code maps the aggregated Scalar UI at `/scalar`.
- Service-local Scalar UIs are mapped at `/docs` for each service.
- Keep compose/env naming aligned with `Backend/Microservices/ApiGateway/src/Program.cs` when editing gateway docs behavior.

## Scalar/OpenAPI docs
- Scalar UI is generated from each service's ASP.NET OpenAPI document (`app.MapOpenApi()` + `app.MapScalarApiReference("docs", ...)`) and aggregated by the API Gateway.
- When adding or changing API endpoints, keep Scalar accurate: add `[ProducesResponseType]` for success, validation/business failures, and unauthorized responses; use request/response DTO records that reflect the actual JSON contract; and keep route names, auth attributes, and HTTP verbs aligned with the controller behavior.
- If an endpoint needs custom request-body or schema behavior, add an OpenAPI transformer under `WebApi/Setups/OpenApi` and register it in `Program.cs`, following the existing Resources multipart transformer pattern.

## Docker & Compose
- `Backend/Compose/docker-compose.yml`: dev stack with placeholders; includes n8n + nginx.
- `Backend/Compose/docker-compose-production.yml`: prod-like stack; treat as sensitive.
- Compose service names use kebab-case (e.g., `ai-microservice`, `user-microservice`, `api-gateway`).
- Postgres init: `Backend/Compose/postgres/init.sql` creates `aidb` and `userdb`.
- Production compose enables `AutoApply__Migrations` and seed data. Running or restarting it can modify runtime state files under `Backend/Compose/seed-data/feed/runtime/*.state.json`; treat those as generated local test artifacts unless the task is specifically about seed state.
- The production user service S3 config currently points at the Terraform backend/app-download bucket. If changing storage behavior, keep Terraform S3 CORS, compose `S3__*`, and FE download behavior aligned.

## Kubernetes
- Manifests in `Backend/Kubernetes/manifests/*.yaml` (Deployments, Services, PVCs, Secrets).
- Local kind cluster config in `Backend/Kubernetes/clusters.yaml` (NodePort mappings).
- EKS manifests can be injected via `k8s_microservices_manifest` in tfvars.
- NodePort defaults are wired into ALB when `use_eks = true`.

## Terraform (AWS)
- Root: `Terraform/` (VPC, ALB, ECS, EKS, RDS, CloudFront/Cloudflare, ACM, SES).
- Platform toggle: `use_eks` in tfvars.
- When `use_eks = true`, Terraform applies `k8s_microservices_manifest` from `k8s.auto.tfvars`.
- When `use_eks = false`, ECS/EC2 uses service groups from `ecs_groups.auto.tfvars`.
- Service definitions live in `Terraform-vars/*-service.auto.tfvars` (sanitized templates).
- Private tfvars live in `terraform-var/` (gitignored); keep sanitized copies in `Terraform-vars/` in sync.
- Scripts: `merge_tfvars.py` (merge), `export_tf_env.py` (export env), `resolve_placeholders.py` (replace `TERRAFORM_*`), `wait_for_services.py` (ECS wait).

## CI/CD (GitHub Actions)
- `full-deploy.yml`: bootstrap backend, build/push images, Terraform plan/apply/destroy.
- `terraform-deploy.yml`: infra-only plan/apply/destroy.
- `build-and-push-ecr.yml`: build/push microservice images to ECR.
- `build-and-push-dockerhub.yml`: build/push images to Docker Hub.
- `bootstrap-terraform-backend.yml`: create S3/DynamoDB backend.
- `acm-certificate.yml`: ACM cert via Cloudflare DNS.
- Destructive: `nuke-aws-except-ecr.yml`, `erase-ecr.yml` (explicit confirmations required).

## Security & secrets
- Do not commit real secrets or keys.
- `terraform-var/` is private and gitignored; keep `Terraform-vars/` sanitized.
- `Backend/Compose/docker-compose-production.yml` contains sensitive values; treat as local-only and rotate if used.
- If you inspect `Backend/Compose/docker-compose-production.yml`, never echo literal secrets back into chat responses, logs, commits, screenshots, or new tracked files.
- `.env` files are ignored; use env vars or tfvars for configuration.

## Adding or changing a service
- New service = new folder with `src/Domain|Application|Infrastructure|WebApi` + `test`.
- Add DI wiring in `WebApi/Program.cs` and update setup extensions in `WebApi/Setups`.
- Add a Dockerfile (`dockerfile` preferred) so compose and CI can detect it.
- Update Terraform service definitions and ECS groups or k8s manifest if the service is deployable.

## What to avoid
- Cross-layer dependencies that violate Clean Architecture rules.
- Storing secrets in tracked files or logs.
- Changing Build/obj or Build/bin conventions unless you update all projects and EF tooling.

## Terraform tfvars parsing (workflow quirk)
- `Terraform/scripts/merge_tfvars.py` runs a `_strip_hcl_quotes()` pass over every HCL-parsed dict before merging, because current `python-hcl2` versions return string values with the HCL quote delimiters embedded (`region = "us-east-1"` → Python string `'"us-east-1"'`). Without this pass, `export_tf_env.py` writes `AWS_REGION="us-east-1"` to `$GITHUB_ENV` — the literal quote chars become part of the value and `aws-actions/configure-aws-credentials@v4` rejects with `Region is not valid: "us-east-1"`. If you ever rewrite the merge step, preserve that strip — HCL strings can never legitimately start AND end with a literal `"`, so stripping one matched outer pair is safe. JSON tfvars bypass the pass entirely (`json.load()` is clean).
- Private tfvars values are not deployed from `terraform-var/` (gitignored); they live as `TERRAFORM_VARS_*` GitHub Environment secrets (one per tfvars file — `TERRAFORM_VARS_K8S` → `k8s.auto.tfvars`, `TERRAFORM_VARS_COMMON` → `common.auto.tfvars`, etc.). Every workflow's "Merge tfvars" step materializes each secret into a file via `jq` — if the secret content is valid JSON the file gets `.auto.tfvars.json`, otherwise `.auto.tfvars` (HCL). Local `terraform-var/` is a working copy only; to deploy a change you must paste the new content into the matching GH secret.
- Corollary: file-path copy differences between workflows don't actually matter for deploy-time values. The nuke workflow copies from `terraform-vars/` (lowercase plural) and `Terraform-vars/` (sanitized template) but not `terraform-var/` (singular); only `full-deploy.yml` copies from `terraform-var/`. Either way, the authoritative values come from GH secrets and overlay the file copies.

## Platform analytics quirks
- `GetSocialMediaPlatformPostAnalyticsQuery` has a snapshot-cache path (`post_metric_snapshots`) that only persists numeric metrics + `PostPayloadJson` — it does NOT persist `CommentSamples`. Facebook, TikTok, and Threads all bypass this cache because they need fresh reply detail; Instagram still uses it. If you add a new platform that returns comment samples, either add it to the cache-bypass list or extend `SocialPlatformPostMetricSnapshotMapper` to serialize comments too.
- Threads `/{id}/replies` is user-scoped (it's the `GET /me/replies` endpoint) and 500s when queried with a post id. Use `/{id}/conversation` for fetching replies to a specific post, then filter out the root by comparing `item.Id` to the queried post id. Avoid requesting the `is_reply` / `root_post` fields — Meta 500s on tokens without the tier/permissions for them.
- Threads reply endpoints require the `threads_read_replies` OAuth scope (read) and/or `threads_manage_replies` (write). Users must re-run the OAuth flow for scope changes to stick — existing access tokens are immutable.
