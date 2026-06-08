# Common Infrastructure Variables
# Updated project name to avoid global S3 bucket name collision.
project_name = "vkev2406-infra-khanghv2406v3"
aws_region   = "ap-southeast-1"
region       = "ap-southeast-1"

# VPC Configuration
vpc_cidr             = "10.0.0.0/16"
public_subnet_cidrs  = ["10.0.1.0/24", "10.0.2.0/24"]
private_subnet_cidrs = ["10.0.3.0/24", "10.0.4.0/24"]

# EC2 Configuration
instance_type       = "t3.micro"
associate_public_ip = true

# ECS Global Settings
enable_auto_scaling = false
use_eks             = true

enable_service_connect = true

# RDS instances to provision
# notification + feed services now exist as separate microservices (docker-compose-production.yml).
# To keep cost low they share the `user` RDS instance â€” each service targets a dedicated db
# (`notificationdb`, `feeddb`) via its own `Database__Name` env var in k8s.auto.tfvars.
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

# Docker Hub pull-through cache (fill in your Secrets Manager ARN for Docker Hub creds)
dockerhub_pull_through_prefix   = "dockerhub"
dockerhub_pull_through_registry = "registry-1.docker.io"

# Docker Hub credentials (username/password or token) used when creating the pull-through cache rule
dockerhub_username = "vkev25811"
dockerhub_password = "REDACTED"

# HTTPS Configuration Options
# 
# OPTION 1: CloudFront HTTPS (Recommended - free, no custom domain required)
use_cloudfront_https      = true
cloudfront_enable_caching = false

# CloudFront Access Logging (set bucket if enabling)
cloudfront_enable_logging          = false
cloudfront_logging_bucket          = "vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state"
cloudfront_logging_prefix          = "cloudfront-logs/"
cloudfront_logging_include_cookies = false

# OPTION 2: ALB with ACM Certificate (requires custom domain)
certificate_arn       = null
enable_https_redirect = true
alb_idle_timeout      = 180

# OPTION 3: Cloudflare (Prioritized over CloudFront if enabled)
use_cloudflare            = true
cloudflare_api_token      = "REDACTED" # Set your API token here
cloudflare_account_id     = "2abe554fae37f20a24006756a2a42b32"         # Set your Account ID here
cloudflare_zone_id        = "24d5daa0f85d5a1552b5d1590ff05f52"         # Set your Zone ID here
domain_name               = "vkev.me"                                  # e.g., example.com
cloudflare_record_name    = "@"                                        # e.g., @ or api
cloudflare_user_email     = "khanghv2406@gmail.com"
cloudflare_global_api_key = "REDACTED" # Set your Cloudflare Global API Key here
# static.vkev.me serves signed media to model providers, so managed AI robots
# restrictions must stay disabled for this zone.
cloudflare_allow_static_ai_fetchers = true

# S3 Static Assets
# static.vkev.me proxies this regional S3 endpoint and preserves the S3 Host
# header so path-style presigned URLs /<bucket>/<key>?X-Amz-* remain valid.
static_assets_bucket_domain_name = "s3.ap-southeast-1.amazonaws.com"
static_assets_cors_allowed_methods = ["GET", "HEAD", "OPTIONS", "PUT"]
