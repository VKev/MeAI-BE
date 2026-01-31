# AGENTS.md MeAI

## Project overview
- .NET 10 microservices template with Clean Architecture per service.
- Services: User, Ai, API Gateway; shared code in SharedLibrary.
- Infra: Docker Compose for local, Terraform for AWS (ECS/EC2 or EKS), optional CloudFront/Cloudflare.

## Repository layout (top-level)
```
Backend/
  Compose/              # docker compose stacks, nginx, postgres init
  Kubernetes/           # manifests and kind cluster config
  Microservices/
    Microservices.sln
    Ai.Microservice/
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
- Controllers are expected to depend on MediatR.
- Tests live at `Backend/Microservices/*/test/ArchitectureTest.cs`.

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
- Default ports (dev/compose): API Gateway 8080 (host 2406), User 5002 (+5004 gRPC), Ai 5001, Postgres 5432, Redis 6379, RabbitMQ 5672/15672, Mailpit 1025/8025, n8n 5678 (via nginx).

## API Gateway (YARP)
- Runtime config is generated in `Backend/Microservices/ApiGateway/src/Setups/YarpRuntimeSetup.cs`.
- Default routes for `/api/user` and `/api/ai`.
- Extra services can be added via `Services__{Service}__Host` and `Services__{Service}__Port` (or `{PREFIX}_MICROSERVICE_HOST/PORT`).
- OpenAPI aggregation pulls `/openapi/v1.json` from each service.
- Runtime config is written to `yarp.runtime.json` in the gateway content root.

## Docker & Compose
- `Backend/Compose/docker-compose.yml`: dev stack with placeholders; includes n8n + nginx.
- `Backend/Compose/docker-compose-production.yml`: prod-like stack; includes Mailpit; treat as sensitive.
- Compose service names use kebab-case (e.g., `ai-microservice`, `user-microservice`, `api-gateway`).
- Postgres init: `Backend/Compose/postgres/init.sql` creates `aidb` and `userdb`.

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
