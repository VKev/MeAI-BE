# MeAI Backend

MeAI-BE is the backend for the MeAI platform: social account connection, post library sync, AI recommendation, AI image/video generation, publishing workflows, notifications, payments, public feed, and RAG-powered account intelligence.

![MeAI backend architecture](./Images/meai-backend-architecture.png)

## What Is Inside

```text
Backend/
  Compose/                         # Local Docker Compose stacks and seed data
  Kubernetes/                      # Local Kubernetes manifests
  Microservices/
    ApiGateway/                    # YARP gateway
    User.Microservice/             # auth, users, OAuth, workspaces, billing, storage
    Ai.Microservice/               # AI generation, recommendations, posts, publish flows
    Feed.Microservice/             # public feed, profiles, analytics, comments
    Notification.Microservice/     # notifications API and SignalR hub
    Rag.Microservice/              # Python RAG service: LightRAG, visual RAG, VideoRAG
    SharedLibrary/                 # shared contracts, auth, middleware, protobuf files
Terraform/                         # AWS infrastructure root and modules
Terraform-vars/                    # redacted Terraform variable templates safe for git
terraform-var/                     # private local variable set with real values; keep out of git
.github/workflows/                 # deploy, update-images, nuke, and Terraform workflows
```

## Architecture

The backend is a microservice system behind `ApiGateway`.

| Service | Tech | Responsibility |
|---|---|---|
| `ApiGateway` | .NET / YARP | Routes `/api/User`, `/api/Ai`, `/api/AiGeneration`, `/api/Feed`, `/api/Notification`; exposes aggregated API docs. |
| `User.Microservice` | .NET | Auth, Google login, social OAuth, account metadata, workspaces, subscriptions, storage resource ownership. |
| `Ai.Microservice` | .NET | AI chats, draft posts, recommendations, post builder, publishing, Kie/Veo callbacks, RAG orchestration. |
| `Feed.Microservice` | .NET | Public MeAI social feed, profiles, posts, comments, analytics and seed data. |
| `Notification.Microservice` | .NET | Notification persistence, REST APIs, SignalR realtime hub. |
| `Rag.Microservice` | Python | Text RAG, image RAG, VideoRAG, account post ingest, knowledge bootstrap. |

Infrastructure dependencies:

- PostgreSQL for service databases.
- Qdrant for RAG vectors.
- RabbitMQ for async events and RAG RPC.
- Redis for cache/coordination.
- S3-compatible object storage for media and RAG image mirroring.
- OpenRouter/Kie/Gemini/Jina/Brave for AI, embeddings, rerank and search.
- Facebook, Instagram, Threads, TikTok, Google, Stripe and email providers for external integrations.

## AWS Infrastructure

Terraform deploys the production backend to AWS EKS. The infrastructure diagram source is in [`Images/meai-aws-eks-infra.drawio`](./Images/meai-aws-eks-infra.drawio).

Main AWS resources:

- VPC with public subnets for the Application Load Balancer and private subnets for EKS nodes and RDS.
- Amazon EKS managed node group for all backend services.
- Kubernetes deployments for `api-gateway`, `user-microservice`, `ai-microservice`, `feed-microservice`, `notification-microservice`, `rag-microservice`, `redis`, `rabbitmq`, and `qdrant`.
- EBS-backed Kubernetes PVCs for Redis, RabbitMQ, Qdrant, and RAG storage.
- Application Load Balancer forwarding public traffic to the EKS API Gateway NodePort.
- Amazon ECR repository for microservice container images.
- PostgreSQL RDS instances for user, notification, feed, and AI databases.
- S3-compatible storage for uploaded media and generated assets.
- Cloudflare DNS/CDN in front of the ALB and static asset domain.
- ACM certificate automation through Cloudflare DNS validation.
- SES resources for email sending.

The request path is:

```text
Client -> Cloudflare -> AWS ALB -> EKS NodePort 32080 -> api-gateway -> microservices
```

Internal service communication uses HTTP through the gateway for public API calls and gRPC for service-to-service calls where configured.

## Kubernetes Setup On AWS EKS

The EKS deployment is controlled by Terraform variables in `k8s.auto.tfvars` and `common.auto.tfvars`.

Required deployment direction:

```hcl
use_eks = true
```

Important EKS settings:

```hcl
eks_cluster_version                 = "1.34"
eks_node_instance_types             = ["t3.large"]
eks_node_min_size                   = 2
eks_node_max_size                   = 5
eks_node_desired_size               = 4
eks_node_capacity_type              = "ON_DEMAND"
eks_default_storage_class_name      = "gp2"
```

Terraform creates the EKS control plane, node group, EBS CSI addon, Kubernetes namespace, storage class, and applies the `k8s_microservices_manifest` from `k8s.auto.tfvars`.

The Kubernetes namespace is based on `project_name`. The API Gateway service is exposed as `NodePort` `32080`; the AWS ALB target group points to this port on the EKS node group.

Persistent workloads:

| Component | PVC | Purpose |
|---|---|---|
| Redis | `redis-data` | cache durability |
| RabbitMQ | `rabbitmq-data` | queue state |
| Qdrant | `qdrant-data` | vector database storage |
| RAG | `rag-data` | local RAG and VideoRAG working data |

## GitHub Environment Setup

Create a GitHub Environment with the same name used in workflow inputs. The current default in workflows is:

```text
infrastructure-khanghv2406
```

Add these environment variables:

| Variable | Purpose |
|---|---|
| `AWS_REGION` | AWS region, for example `ap-southeast-1`. |
| `PROJECT_NAME` | Terraform project name. Used for resource names, EKS namespace, state bucket, and tags. |
| `ECR_REPO_NAME` | Shared ECR repository name. If empty, workflows derive it from project/environment. |

Add these environment secrets:

| Secret | Purpose |
|---|---|
| `AWS_ACCESS_KEY_ID` | AWS deploy access key. |
| `AWS_SECRET_ACCESS_KEY` | AWS deploy secret key. |
| `CLOUDFLARE_API_TOKEN` | Required for Cloudflare DNS, ACM validation, records, and static proxy resources. |
| `DOCKERHUB_USERNAME` | Only required by the Docker Hub workflow. |
| `DOCKERHUB_TOKEN` | Only required by the Docker Hub workflow. |

Terraform variables can be provided through `TERRAFORM_VARS_*` environment secrets. The workflows merge values from `Terraform-vars/`, `terraform-var/`, and these secrets before running Terraform. Use GitHub Environment secrets for real production values.

Common secret names:

```text
TERRAFORM_VARS_COMMON_AUTO_TFVARS
TERRAFORM_VARS_K8S_AUTO_TFVARS
TERRAFORM_VARS_APIGATEWAY_SERVICE_AUTO_TFVARS
TERRAFORM_VARS_AI_SERVICE_AUTO_TFVARS
TERRAFORM_VARS_USER_SERVICE_AUTO_TFVARS
TERRAFORM_VARS_REDIS_SERVICE_AUTO_TFVARS
TERRAFORM_VARS_RABBITMQ_SERVICE_AUTO_TFVARS
TERRAFORM_VARS_ECS_GROUPS_AUTO_TFVARS
```

Each secret can contain HCL text. A JSON variant is also supported by adding `_JSON` to the secret name, for example:

```text
TERRAFORM_VARS_COMMON_AUTO_TFVARS_JSON
```

Example HCL secret content:

```hcl
project_name = "your-project"
aws_region   = "ap-southeast-1"
region       = "ap-southeast-1"
use_eks      = true

rds = {
  user = {
    db_names          = ["userdb", "notificationdb", "feeddb"]
    username          = "avnadmin"
    engine_version    = "18.1"
    instance_class    = "db.t3.micro"
    password          = "REDACTED"
    allocated_storage = 5
  }
  ai = {
    db_names          = ["aidb"]
    username          = "avnadmin"
    engine_version    = "18.1"
    instance_class    = "db.t3.micro"
    password          = "REDACTED"
    allocated_storage = 5
  }
}
```

Do not commit real secrets to `Terraform-vars/`. Keep committed files redacted. Keep real local values in `terraform-var/` or GitHub Environment secrets.

## How To Deploy

Recommended first deployment:

1. Create the GitHub Environment and add the variables/secrets above.
2. Run `Bootstrap Terraform Backend` once to create the S3 Terraform state bucket and DynamoDB lock table.
3. Run `Full Infrastructure Deploy` with `terraform_action = apply`.
4. After the first deployment, use `Update Deployed EKS Images` for normal image rollouts.

GitHub Actions:

| Workflow | File | Use |
|---|---|---|
| Bootstrap Terraform Backend | `.github/workflows/bootstrap-terraform-backend.yml` | Creates the remote Terraform state bucket and lock table. Run once per environment/project. |
| Full Infrastructure Deploy | `.github/workflows/full-deploy.yml` | Builds all microservice images to ECR, then runs Terraform `plan`, `apply`, or `destroy`. This is the main deploy workflow. |
| Deploy Infrastructure with Terraform | `.github/workflows/terraform-deploy.yml` | Runs only Terraform `plan`, `apply`, or `destroy` using the merged tfvars. Use for infra-only changes. |
| Build and Push Microservices to ECR | `.github/workflows/build-and-push-ecr.yml` | Builds service Docker images and pushes them to ECR without applying Terraform. |
| Update Deployed EKS Images | `.github/workflows/update-deployed-images.yml` | Builds selected services and patches running EKS deployments to the new image tags. Can optionally clear namespace PVCs and logically wipe RDS data when confirmations are provided. |
| Request ACM Certificate | `.github/workflows/acm-certificate.yml` | Requests or destroys ACM certificates using Cloudflare DNS validation. |
| Build and Push DockerHub | `.github/workflows/build-and-push-dockerhub.yml` | Optional Docker Hub image publishing workflow. EKS deploy uses ECR. |
| Nuke ECR Repositories | `.github/workflows/erase-ecr.yml` | Destructive ECR cleanup. Requires `confirm = ERASE`. |
| Nuke AWS (except ECR) | `.github/workflows/nuke-aws-except-ecr.yml` | Destructive AWS cleanup. Requires `confirm = NUKE`. ECR is preserved. |

For `Update Deployed EKS Images`, service names can be `all` or a comma-separated list such as:

```text
ApiGateway,User.Microservice,Ai.Microservice,Feed.Microservice,Notification.Microservice,Rag.Microservice
```

The workflow maps these to Kubernetes deployments:

| Service | Kubernetes deployment |
|---|---|
| `ApiGateway` | `api-gateway` |
| `User.Microservice` | `user-microservice` |
| `Ai.Microservice` | `ai-microservice` |
| `Feed.Microservice` | `feed-microservice` |
| `Notification.Microservice` | `notification-microservice` |
| `Rag.Microservice` | `rag-microservice` |

Destructive options require exact confirmations:

```text
fresh_start_confirmation = DELETE_DEPLOYED_DATA
rds_data_confirmation    = DELETE_RDS_DATA
```

Use these only when intentionally clearing deployed data.
