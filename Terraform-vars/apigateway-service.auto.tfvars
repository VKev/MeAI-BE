# This file is automatically sanitized.
# Run scripts/sanitize_tfvars.py after editing real tfvars.

services = {
  apigateway = {
    alb_target_group_port     = 8080
    alb_target_group_protocol = "HTTP"
    alb_target_group_type     = "ip"
    alb_health_check = {
      enabled             = true
      path                = "/health"
      port                = "traffic-port"
      protocol            = "HTTP"
      matcher             = "200"
      interval            = 30
      timeout             = 5
      healthy_threshold   = 2
      unhealthy_threshold = 3
    }
    # No listener rule needed â€” api-gateway is already the ALB's default_listener_action
    # target in main_alb.tf. Adding a priority-100 rule with empty conditions would
    # either be rejected at plan time (AWS requires at least one condition per rule) or
    # produce a duplicate forward to the same target group.
    alb_listener_rule_priority         = null
    alb_listener_rule_conditions       = []
    ecs_service_connect_dns_name       = "api-gateway"
    ecs_service_connect_discovery_name = "api-gateway"
    ecs_service_connect_port_name      = "apigateway"
    ecs_container_name_suffix          = "apigateway"
    ecs_container_image_repository_url = "784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr"
    ecs_container_image_tag            = "ApiGateway-latest"
    ecs_container_cpu                  = 256
    ecs_container_memory               = 256
    ecs_container_essential            = true
    ecs_container_port_mappings = [
      {
        container_port = 8080
        host_port      = 0
        protocol       = "tcp"
        name           = "apigateway"
      }
    ]
    ecs_environment_variables = [
      {
        name  = "ENABLE_SWAGGER_UI"
        value = "true"
      },
      {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      },
      {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      },
      {
        name  = "BASE_URL"
        value = "https://vkev.me"
      },
      {
        name  = "Services__User__Host"
        value = "user-service"
      },
      {
        name  = "Services__User__Port"
        value = "5002"
      },
      {
        name  = "Services__Ai__Host"
        value = "ai-service"
      },
      {
        name  = "Services__Ai__Port"
        value = "5001"
      },
      { name = "Jwt__SecretKey", value = "YourSuperSecretKeyThatIsAtLeast32CharactersLong!@#$%^&*()" },
      { name = "Jwt__Issuer", value = "UserMicroservice" },
      { name = "Jwt__Audience", value = "MicroservicesApp" },
      {
        name  = "Jwt__ExpirationMinutes"
        value = "60"
      },
      {
        name  = "Cors__AllowedOrigins__0"
        value = "http://localhost:5173"
      },
      {
        name  = "Cors__AllowedOrigins__1"
        value = "https://your-frontend.example.com"
      },
      {
        name  = "Cors__AllowedOrigins__2"
        value = "https://vkev.me"
      }
    ]
    ecs_container_health_check = {
      command = [
        "CMD-SHELL",
        "curl -f http://localhost:8080/health || exit 1"
      ]
      interval    = 30
      timeout     = 5
      retries     = 3
      startPeriod = 10
    }
    depends_on = [
      "user-microservice"
    ]
  }
}
