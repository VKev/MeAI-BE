
# EKS Cluster Configuration
eks_cluster_version                          = "1.34"
eks_cluster_endpoint_public_access           = true
eks_cluster_endpoint_private_access          = true
eks_node_instance_types                      = ["t3.large"]
eks_node_min_size                            = 2
eks_node_max_size                            = 5
eks_node_desired_size                        = 4
eks_node_capacity_type                       = "ON_DEMAND"
environment                                  = "dev"
eks_enable_cluster_creator_admin_permissions = true
eks_create_cloudwatch_log_group              = false
eks_default_storage_class_name               = "gp2"
eks_ebs_volume_type                          = "gp2"


k8s_resources = {
  storage_class = "gp2" # Use EKS default gp2 StorageClass

  redis = {
    replicas = 1
    requests = { cpu = "50m", memory = "128Mi" }
    limits   = { cpu = "250m", memory = "256Mi" }
  }

  rabbitmq = {
    replicas = 1
    requests = { cpu = "250m", memory = "512Mi" }
    limits   = { cpu = "1000m", memory = "1024Mi" }
  }

  ai = {
    replicas = 1
    requests = { cpu = "300m", memory = "512Mi" }
    limits   = { cpu = "1000m", memory = "1536Mi" }
  }

  user = {
    replicas = 1
    requests = { cpu = "300m", memory = "512Mi" }
    limits   = { cpu = "1000m", memory = "1536Mi" }
  }

  apigateway = {
    replicas = 1
    requests = { cpu = "250m", memory = "384Mi" }
    limits   = { cpu = "1000m", memory = "1024Mi" }
  }

  notification = {
    replicas = 1
    requests = { cpu = "200m", memory = "384Mi" }
    limits   = { cpu = "750m", memory = "768Mi" }
  }

  feed = {
    replicas = 1
    requests = { cpu = "200m", memory = "384Mi" }
    limits   = { cpu = "750m", memory = "768Mi" }
  }
  qdrant = {
    replicas = 1
    requests = { cpu = "500m", memory = "1024Mi" }
    limits   = { cpu = "1500m", memory = "2048Mi" }
  }

  rag = {
    replicas = 1
    requests = { cpu = "500m", memory = "1536Mi" }
    limits   = { cpu = "2000m", memory = "3072Mi" }
  }
}

k8s_microservices_manifest = <<-EOT
apiVersion: v1
kind: Namespace
metadata:
  name: microservices
---
apiVersion: v1
kind: Secret
metadata:
  name: redis-auth
  namespace: TERRAFORM_NAMESPACE
type: Opaque
stringData:
  redis-password: "REDACTED"
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: redis-data
  namespace: TERRAFORM_NAMESPACE
spec:
  storageClassName: gp2
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: redis
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
        - name: redis
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/dockerhub/library/redis:alpine
          ports:
            - containerPort: 6379
          env:
            - name: REDIS_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: redis-auth
                  key: redis-password
          resources:
            requests:
              cpu: 50m
              memory: 128Mi
            limits:
              cpu: 250m
              memory: 256Mi
          command: ['sh', '-c', 'exec redis-server --requirepass "$REDIS_PASSWORD"']
          livenessProbe:
            exec:
              command: ['sh', '-c', 'redis-cli -a "$REDIS_PASSWORD" ping']
            initialDelaySeconds: 10
            periodSeconds: 10
          readinessProbe:
            exec:
              command: ['sh', '-c', 'redis-cli -a "$REDIS_PASSWORD" ping']
            initialDelaySeconds: 5
            periodSeconds: 10
          volumeMounts:
            - name: redis-data
              mountPath: /data
      volumes:
        - name: redis-data
          persistentVolumeClaim:
            claimName: redis-data
---
apiVersion: v1
kind: Service
metadata:
  name: redis
  namespace: TERRAFORM_NAMESPACE
spec:
  type: NodePort
  selector:
    app: redis
  ports:
    - port: 6379
      targetPort: 6379
      nodePort: 30379
---
apiVersion: v1
kind: Secret
metadata:
  name: rabbitmq-auth
  namespace: TERRAFORM_NAMESPACE
type: Opaque
stringData:
  rabbitmq-username: "REDACTED"
  rabbitmq-password: "REDACTED"
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: rabbitmq-data
  namespace: TERRAFORM_NAMESPACE
spec:
  storageClassName: gp2
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rabbitmq
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: rabbitmq
  template:
    metadata:
      labels:
        app: rabbitmq
    spec:
      securityContext:
        runAsUser: 999
        runAsGroup: 999
        fsGroup: 999
        fsGroupChangePolicy: "OnRootMismatch"
      containers:
        - name: rabbit-mq
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/dockerhub/library/rabbitmq:3-management
          ports:
            - containerPort: 5672
            - containerPort: 15672
          startupProbe:
            exec:
              command: ["rabbitmqctl", "status"]
            initialDelaySeconds: 15
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 6
          env:
            - name: RABBITMQ_DEFAULT_USER
              valueFrom:
                secretKeyRef:
                  name: rabbitmq-auth
                  key: rabbitmq-username
            - name: RABBITMQ_DEFAULT_PASS
              valueFrom:
                secretKeyRef:
                  name: rabbitmq-auth
                  key: rabbitmq-password
          resources:
            requests:
              cpu: 250m
              memory: 512Mi
            limits:
              cpu: 1000m
              memory: 1024Mi
          livenessProbe:
            exec:
              command: ["rabbitmq-diagnostics", "-q", "ping"]
            initialDelaySeconds: 120
            periodSeconds: 30
            timeoutSeconds: 5
            failureThreshold: 3
          readinessProbe:
            exec:
              command: ["rabbitmq-diagnostics", "-q", "ping"]
            initialDelaySeconds: 30
            periodSeconds: 15
            timeoutSeconds: 5
            failureThreshold: 15
          volumeMounts:
            - name: rabbitmq-data
              mountPath: /var/lib/rabbitmq
      volumes:
        - name: rabbitmq-data
          persistentVolumeClaim:
            claimName: rabbitmq-data
---
apiVersion: v1
kind: Service
metadata:
  name: rabbit-mq
  namespace: TERRAFORM_NAMESPACE
spec:
  type: NodePort
  selector:
    app: rabbitmq
  ports:
    - name: amqp
      port: 5672
      targetPort: 5672
      nodePort: 30672
    - name: management
      port: 15672
      targetPort: 15672
      nodePort: 31672
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: feed-seed-data
  namespace: TERRAFORM_NAMESPACE
spec:
  storageClassName: gp2
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ai-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: ai-microservice
  template:
    metadata:
      labels:
        app: ai-microservice
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                topologyKey: kubernetes.io/hostname
                labelSelector:
                  matchLabels:
                    app: ai-microservice
      containers:
        - name: ai-microservice
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr:Ai.Microservice-latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 5001
            - containerPort: 5005
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
            - name: ASPNETCORE_URLS
              value: http://+:5001
            - name: Database__Host
              value: "TERRAFORM_RDS_HOST_AI"
            - name: Database__Port
              value: "TERRAFORM_RDS_PORT_AI"
            - name: Database__Name
              value: "TERRAFORM_RDS_DB_AI"
            - name: Database__Username
              value: "REDACTED"
            - name: Database__Password
              value: "REDACTED"
            - name: Database__Provider
              value: "TERRAFORM_RDS_PROVIDER_AI"
            - name: Database__SslMode
              value: "TERRAFORM_RDS_SSLMODE_AI"
            - name: RabbitMq__Host
              value: rabbit-mq
            - name: RabbitMq__Port
              value: "5672"
            - name: RabbitMq__Username
              value: "REDACTED"
            - name: RabbitMq__Password
              value: "REDACTED"
            - name: Redis__Host
              value: redis
            - name: Redis__Password
              value: "REDACTED"
            - name: Redis__Port
              value: "6379"
            - name: Jwt__SecretKey
              value: "REDACTED"
            - name: Jwt__Issuer
              value: UserMicroservice
            - name: Jwt__Audience
              value: MicroservicesApp
            - name: Jwt__ExpirationMinutes
              value: "3600"
            # CORS: "*" sentinel triggers wildcard mode in CorsSetup.cs Ã¢â‚¬â€ policy uses
            # SetIsOriginAllowed(_ => true) which echoes the request Origin back, keeping
            # AllowCredentials compatible with arbitrary origins.
            - name: Cors__AllowedOrigins__0
              value: "*"
            - name: UserService__GrpcUrl
              value: http://user-microservice:5004
            - name: AutoApply__Migrations
              value: "true"
            - name: SampleSeed__Enabled
              value: "true"
            - name: SampleSeed__DataRoot
              value: /seed-data
            - name: Kie__ApiKey
              value: "REDACTED"
            - name: Kie__CallbackUrl
              value: "REDACTED"
            - name: Gemini__ApiKey
              value: "REDACTED"
            - name: Cors__AllowedOrigins__1
              value: "http://localhost:3030"
            - name: Cors__AllowedOrigins__2
              value: "https://meaiplatform.io.vn"
            - name: Rag__IngestQueue
              value: "meai.rag.ingest"
            - name: Rag__QueryQueue
              value: "meai.rag.query"
            - name: Rag__RpcTimeoutSeconds
              value: "60"
            - name: Rag__WaitReadyTimeoutSeconds
              value: "1800"
            - name: Rag__GrpcUrl
              value: "http://rag-microservice:5006"
            - name: Rag__GrpcIngestTimeoutSeconds
              value: "300"
            - name: Rag__S3PublicBaseUrl
              value: "https://static.vkev.me"
            - name: Rag__MultimodalLlmBaseUrl
              value: "https://openrouter.ai/api/v1"
            - name: Rag__MultimodalLlmApiKey
              value: "REDACTED"
            - name: Rag__MultimodalLlmModel
              value: "openai/gpt-4o-mini"
            - name: Rag__MultimodalAnswerTimeoutSeconds
              value: "60"
            - name: Rag__WebSearchEnabled
              value: "true"
            - name: Rag__WebSearchMaxResults
              value: "5"
            - name: Rag__BraveSearchApiKey
              value: "REDACTED"
            - name: Rag__BraveImageSearchCountry
              value: "REDACTED"
            - name: Rag__BraveImageSearchSafe
              value: "REDACTED"
            - name: Rag__RerankBaseUrl
              value: "https://api.jina.ai/v1/rerank"
            - name: Rag__RerankModel
              value: "jina-reranker-m0"
            - name: Rag__RerankApiKey
              value: "REDACTED"
            - name: FeedService__GrpcUrl
              value: "http://feed-microservice:5008"
          resources:
            requests:
              cpu: 300m
              memory: 512Mi
            limits:
              cpu: 1000m
              memory: 1536Mi
---
apiVersion: v1
kind: Service
metadata:
  name: ai-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  selector:
    app: ai-microservice
  ports:
    - name: http
      port: 5001
      targetPort: 5001
    - name: grpc
      # Ai.Microservice Program.cs listens on 5005 for gRPC. Must match feed-microservice's
      # AiService__GrpcUrl and user-microservice's AiService__GrpcUrl Ã¢â‚¬â€ both point at :5005.
      port: 5005
      targetPort: 5005
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: user-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: user-microservice
  template:
    metadata:
      labels:
        app: user-microservice
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                topologyKey: kubernetes.io/hostname
                labelSelector:
                  matchLabels:
                    app: user-microservice
      containers:
        - name: user-microservice
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr:User.Microservice-latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 5002
            - containerPort: 5004
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
            - name: ASPNETCORE_URLS
              value: http://+:5002;http://+:5004
            - name: Database__Host
              value: "TERRAFORM_RDS_HOST_USER"
            - name: Database__Port
              value: "TERRAFORM_RDS_PORT_USER"
            - name: Database__Name
              value: "TERRAFORM_RDS_DB_USER"
            - name: Database__Username
              value: "REDACTED"
            - name: Database__Password
              value: "REDACTED"
            - name: Database__Provider
              value: "TERRAFORM_RDS_PROVIDER_USER"
            - name: Database__SslMode
              value: "TERRAFORM_RDS_SSLMODE_USER"
            - name: RabbitMq__Host
              value: rabbit-mq
            - name: RabbitMq__Port
              value: "5672"
            - name: RabbitMq__Username
              value: "REDACTED"
            - name: RabbitMq__Password
              value: "REDACTED"
            - name: Redis__Host
              value: redis
            - name: Redis__Password
              value: "REDACTED"
            - name: Redis__Port
              value: "6379"
            - name: AiService__GrpcUrl
              value: "http://ai-microservice:5005"
            - name: UserService__GrpcUrl
              value: http://user-microservice:5004
            - name: Jwt__SecretKey
              value: "REDACTED"
            - name: Jwt__Issuer
              value: UserMicroservice
            - name: Jwt__Audience
              value: MicroservicesApp
            - name: Jwt__ExpirationMinutes
              value: "3600"
            # CORS wildcard Ã¢â‚¬â€ see CorsSetup.cs. "*" triggers SetIsOriginAllowed(_ => true).
            - name: Cors__AllowedOrigins__0
              value: "*"
            - name: Google__ClientId
              value: "REDACTED"
            - name: Facebook__AppId
              value: "REDACTED"
            - name: Facebook__AppSecret
              value: "REDACTED"
            # OAuth callbacks land on the FE first Ã¢â‚¬â€ the FE forwards the `code` to the BE
            # via a client-side API call. Keep these FE URLs in sync with the routes in
            # MeAI-FE's `app/routes/auth/*/callback.tsx`.
            - name: Facebook__RedirectUri
              value: "REDACTED"
            - name: Facebook__Scopes
              value: "REDACTED"
            - name: Instagram__RedirectUri
              value: "REDACTED"
            - name: Instagram__Scopes
              value: "REDACTED"
            - name: Email__Host
              value: "REDACTED"
            - name: Email__Port
              value: "REDACTED"
            - name: Email__Username
              value: "REDACTED"
            - name: Email__Password
              value: "REDACTED"
            - name: Email__UseStartTls
              value: "REDACTED"
            - name: Email__UseSsl
              value: "REDACTED"
            - name: Email__DisableCertificateRevocationCheck
              value: "REDACTED"
            - name: Email__FromEmail
              value: "REDACTED"
            - name: Email__FromName
              value: "REDACTED"
            - name: Admin__Username
              value: admin
            - name: Admin__Password
              value: "REDACTED"
            - name: Admin__Email
              value: admin@gmail.com
            - name: Admin__FullName
              value: MeAI Admin
            - name: DefaultUser__Username
              value: user
            - name: DefaultUser__Password
              value: "REDACTED"
            - name: DefaultUser__Email
              value: user@gmail.com
            - name: DefaultUser__FullName
              value: MeAI User
            - name: DefaultUser__RoleName
              value: USER
            - name: AutoApply__Migrations
              value: "true"
            - name: SampleSeed__Enabled
              value: "true"
            - name: SampleSeed__DataRoot
              value: /seed-data
            - name: FeedSeed__Enabled
              value: "true"
            - name: FeedSeed__DataRoot
              value: /feed-seed-data
            - name: FeedSeed__PublicBaseUrl
              value: https://vkev.me
            - name: TikTok__ClientKey
              value: "REDACTED"
            - name: TikTok__ClientSecret
              value: "REDACTED"
            - name: TikTok__RedirectUri
              value: "REDACTED"
            - name: Threads__AppId
              value: "REDACTED"
            - name: Threads__AppSecret
              value: "REDACTED"
            - name: Threads__RedirectUri
              value: "REDACTED"
            # threads_read_replies is required for /conversation (comment detail in analytics);
            # threads_manage_replies covers write. Scope changes require users to re-run OAuth.
            - name: Threads__Scopes
              value: "REDACTED"
            - name: Stripe__PublishableKey
              value: "REDACTED"
            - name: Stripe__SecretKey
              value: "REDACTED"
            - name: Stripe__WebhookSecret
              value: "REDACTED"
            - name: S3__Bucket
              value: vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state
            - name: S3__Region
              value: ap-southeast-1
            - name: S3__ServiceUrl
              value: https://s3.ap-southeast-1.amazonaws.com
            - name: S3__PublicBaseUrl
              value: https://static.vkev.me
            - name: S3__AccessKey
              value: "REDACTED"
            - name: S3__SecretKey
              value: "REDACTED"
            - name: Cors__AllowedOrigins__1
              value: "http://localhost:3000"
            - name: Cors__AllowedOrigins__2
              value: "http://localhost:3030"
            - name: Cors__AllowedOrigins__3
              value: "https://meaiplatform.io.vn"
            - name: S3__Namespace
              value: "meai"
          resources:
            requests:
              cpu: 300m
              memory: 512Mi
            limits:
              cpu: 1000m
              memory: 1536Mi
          volumeMounts:
            - name: feed-seed-data
              mountPath: /feed-seed-data
      volumes:
        - name: feed-seed-data
          persistentVolumeClaim:
            claimName: feed-seed-data
---
apiVersion: v1
kind: Service
metadata:
  name: user-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  selector:
    app: user-microservice
  ports:
    - name: http
      port: 5002
      targetPort: 5002
    - name: grpc
      port: 5004
      targetPort: 5004
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-gateway
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: api-gateway
  template:
    metadata:
      labels:
        app: api-gateway
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                topologyKey: kubernetes.io/hostname
                labelSelector:
                  matchLabels:
                    app: api-gateway
      containers:
        - name: api-gateway
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr:ApiGateway-latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 8080
          env:
            - name: ENABLE_DOCS_UI
              value: "true"
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
            - name: ASPNETCORE_URLS
              value: http://+:8080
            # CORS wildcard Ã¢â‚¬â€ see ApiGateway/src/Setups/CorsSetup.cs. "*" triggers
            # SetIsOriginAllowed(_ => true) while keeping AllowCredentials working.
            - name: Cors__AllowedOrigins__0
              value: "*"
            - name: Services__User__Host
              value: user-microservice
            - name: Services__User__Port
              value: "5002"
            - name: Services__Ai__Host
              value: ai-microservice
            - name: Services__Ai__Port
              value: "5001"
            - name: Services__Notification__Host
              value: notification-microservice
            - name: Services__Notification__Port
              value: "5006"
            - name: Services__Feed__Host
              value: feed-microservice
            - name: Services__Feed__Port
              value: "5007"
            - name: Jwt__SecretKey
              value: "REDACTED"
            - name: Jwt__Issuer
              value: UserMicroservice
            - name: Jwt__Audience
              value: MicroservicesApp
            - name: Jwt__ExpirationMinutes
              value: "3600"
            - name: Cors__AllowedOrigins__1
              value: "http://localhost:3000"
            - name: Cors__AllowedOrigins__2
              value: "http://localhost:3030"
            - name: Cors__AllowedOrigins__3
              value: "https://meaiplatform.io.vn"
          resources:
            requests:
              cpu: 250m
              memory: 384Mi
            limits:
              cpu: 1000m
              memory: 1024Mi
---
apiVersion: v1
kind: Service
metadata:
  name: api-gateway
  namespace: TERRAFORM_NAMESPACE
spec:
  type: NodePort
  selector:
    app: api-gateway
  ports:
    - port: 8080
      targetPort: 8080
      nodePort: 32080
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: notification-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: notification-microservice
  template:
    metadata:
      labels:
        app: notification-microservice
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                topologyKey: kubernetes.io/hostname
                labelSelector:
                  matchLabels:
                    app: notification-microservice
      containers:
        - name: notification-microservice
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr:Notification.Microservice-latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 5006
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
            - name: ASPNETCORE_URLS
              value: http://+:5006
            # Notification + feed share the `user` RDS instance but target their own
            # dedicated databases (`notificationdb`, `feeddb`). Added to rds.user.db_names
            # in common.auto.tfvars.
            - name: Database__Host
              value: "TERRAFORM_RDS_HOST_USER"
            - name: Database__Port
              value: "TERRAFORM_RDS_PORT_USER"
            - name: Database__Name
              value: "notificationdb"
            - name: Database__Username
              value: "REDACTED"
            - name: Database__Password
              value: "REDACTED"
            - name: Database__Provider
              value: "TERRAFORM_RDS_PROVIDER_USER"
            - name: Database__SslMode
              value: "TERRAFORM_RDS_SSLMODE_USER"
            - name: RabbitMq__Host
              value: rabbit-mq
            - name: RabbitMq__Port
              value: "5672"
            - name: RabbitMq__Username
              value: "REDACTED"
            - name: RabbitMq__Password
              value: "REDACTED"
            - name: Redis__Host
              value: redis
            - name: Redis__Password
              value: "REDACTED"
            - name: Redis__Port
              value: "6379"
            - name: Jwt__SecretKey
              value: "REDACTED"
            - name: Jwt__Issuer
              value: UserMicroservice
            - name: Jwt__Audience
              value: MicroservicesApp
            - name: Jwt__ExpirationMinutes
              value: "3600"
            # CORS wildcard Ã¢â‚¬â€ see CorsSetup.cs.
            - name: Cors__AllowedOrigins__0
              value: "*"
            - name: AutoApply__Migrations
              value: "true"
            - name: Cors__AllowedOrigins__1
              value: "http://localhost:3000"
            - name: Cors__AllowedOrigins__2
              value: "http://localhost:3030"
            - name: Cors__AllowedOrigins__3
              value: "https://meaiplatform.io.vn"
          resources:
            requests:
              cpu: 200m
              memory: 384Mi
            limits:
              cpu: 750m
              memory: 768Mi
---
apiVersion: v1
kind: Service
metadata:
  name: notification-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  selector:
    app: notification-microservice
  ports:
    - name: http
      port: 5006
      targetPort: 5006
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: feed-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: feed-microservice
  template:
    metadata:
      labels:
        app: feed-microservice
    spec:
      affinity:
        podAffinity:
          requiredDuringSchedulingIgnoredDuringExecution:
            - topologyKey: kubernetes.io/hostname
              labelSelector:
                matchLabels:
                  app: user-microservice
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                topologyKey: kubernetes.io/hostname
                labelSelector:
                  matchLabels:
                    app: feed-microservice
      containers:
        - name: feed-microservice
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr:Feed.Microservice-latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 5007
            - containerPort: 5008
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
            - name: ASPNETCORE_URLS
              value: http://+:5007;http://+:5008
            - name: Database__Host
              value: "TERRAFORM_RDS_HOST_USER"
            - name: Database__Port
              value: "TERRAFORM_RDS_PORT_USER"
            - name: Database__Name
              value: "feeddb"
            - name: Database__Username
              value: "REDACTED"
            - name: Database__Password
              value: "REDACTED"
            - name: Database__Provider
              value: "TERRAFORM_RDS_PROVIDER_USER"
            - name: Database__SslMode
              value: "TERRAFORM_RDS_SSLMODE_USER"
            - name: RabbitMq__Host
              value: rabbit-mq
            - name: RabbitMq__Port
              value: "5672"
            - name: RabbitMq__Username
              value: "REDACTED"
            - name: RabbitMq__Password
              value: "REDACTED"
            - name: Redis__Host
              value: redis
            - name: Redis__Password
              value: "REDACTED"
            - name: Redis__Port
              value: "6379"
            - name: UserService__GrpcUrl
              value: http://user-microservice:5004
            - name: AiService__GrpcUrl
              value: "http://ai-microservice:5005"
            - name: Jwt__SecretKey
              value: "REDACTED"
            - name: Jwt__Issuer
              value: UserMicroservice
            - name: Jwt__Audience
              value: MicroservicesApp
            - name: Jwt__ExpirationMinutes
              value: "3600"
            # CORS wildcard Ã¢â‚¬â€ see CorsSetup.cs.
            - name: Cors__AllowedOrigins__0
              value: "*"
            - name: AutoApply__Migrations
              value: "true"
            - name: FeedSeed__Enabled
              value: "true"
            - name: FeedSeed__DataRoot
              value: /feed-seed-data
            - name: FeedSeed__PublicBaseUrl
              value: https://vkev.me
            - name: Cors__AllowedOrigins__1
              value: "http://localhost:3000"
            - name: Cors__AllowedOrigins__2
              value: "http://localhost:3030"
            - name: Cors__AllowedOrigins__3
              value: "https://meaiplatform.io.vn"
          resources:
            requests:
              cpu: 200m
              memory: 384Mi
            limits:
              cpu: 750m
              memory: 768Mi
          volumeMounts:
            - name: feed-seed-data
              mountPath: /feed-seed-data
      volumes:
        - name: feed-seed-data
          persistentVolumeClaim:
            claimName: feed-seed-data
---
apiVersion: v1
kind: Service
metadata:
  name: feed-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  selector:
    app: feed-microservice
  ports:
    - name: http
      port: 5007
      targetPort: 5007
    - name: grpc
      port: 5008
      targetPort: 5008
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: qdrant-data
  namespace: TERRAFORM_NAMESPACE
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: gp2
  resources:
    requests:
      storage: 10Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: qdrant
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: qdrant
  template:
    metadata:
      labels:
        app: qdrant
    spec:
      containers:
        - name: qdrant
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/dockerhub/qdrant/qdrant:latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 6333
            - containerPort: 6334
          volumeMounts:
            - name: qdrant-data
              mountPath: /qdrant/storage
          readinessProbe:
            tcpSocket:
              port: 6333
            initialDelaySeconds: 5
            periodSeconds: 10
            timeoutSeconds: 3
            failureThreshold: 12
          livenessProbe:
            tcpSocket:
              port: 6333
            initialDelaySeconds: 30
            periodSeconds: 30
            timeoutSeconds: 3
            failureThreshold: 6
          resources:
            requests:
              cpu: 500m
              memory: 1024Mi
            limits:
              cpu: 1500m
              memory: 2048Mi
      volumes:
        - name: qdrant-data
          persistentVolumeClaim:
            claimName: qdrant-data
---
apiVersion: v1
kind: Service
metadata:
  name: qdrant
  namespace: TERRAFORM_NAMESPACE
spec:
  selector:
    app: qdrant
  ports:
    - name: http
      port: 6333
      targetPort: 6333
    - name: grpc
      port: 6334
      targetPort: 6334
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: rag-data
  namespace: TERRAFORM_NAMESPACE
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: gp2
  resources:
    requests:
      storage: 10Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rag-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  replicas: 1
  selector:
    matchLabels:
      app: rag-microservice
  template:
    metadata:
      labels:
        app: rag-microservice
    spec:
      initContainers:
        - name: wait-for-qdrant
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/dockerhub/library/busybox:1.36
          command:
            - sh
            - -c
            - |
              until nc -z qdrant 6333; do
                echo "waiting for qdrant:6333"
                sleep 3
              done
        - name: wait-for-rabbitmq
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/dockerhub/library/busybox:1.36
          command:
            - sh
            - -c
            - |
              until nc -z rabbit-mq 5672; do
                echo "waiting for rabbit-mq:5672"
                sleep 3
              done
      containers:
        - name: rag-microservice
          image: 784180969479.dkr.ecr.ap-southeast-1.amazonaws.com/vkev2406-infra-khanghv2406v3-infrastructure-khanghv2406-ecr:Rag.Microservice-latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 8000
            - containerPort: 5006
          env:
            - name: LOG_LEVEL
              value: "INFO"
            - name: WORKING_DIR
              value: "/data/rag_storage"
            - name: KNOWLEDGE_DIR
              value: "/app/src/knowledge"
            - name: RABBITMQ_HOST
              value: "rabbit-mq"
            - name: RABBITMQ_PORT
              value: "5672"
            - name: RABBITMQ_USER
              value: "rabbitmq"
            - name: RABBITMQ_PASS
              value: "0Kg04Rq08!"
            - name: RABBIT_INGEST_QUEUE
              value: "meai.rag.ingest"
            - name: RABBIT_QUERY_QUEUE
              value: "meai.rag.query"
            - name: RABBIT_PREFETCH
              value: "4"
            - name: QDRANT_URL
              value: "http://qdrant:6333"
            - name: QDRANT_NAMESPACE
              value: "meai_rag"
            - name: LLM_BASE_URL
              value: "https://openrouter.ai/api/v1"
            - name: LLM_API_KEY
              value: "REDACTED"
            - name: LLM_MODEL
              value: "openai/gpt-4o-mini"
            - name: EMBED_BASE_URL
              value: "https://openrouter.ai/api/v1"
            - name: EMBED_API_KEY
              value: "REDACTED"
            - name: EMBED_MODEL
              value: "openai/text-embedding-3-small"
            - name: EMBED_DIM
              value: "1536"
            - name: MULTIMODAL_EMBED_BASE_URL
              value: "https://openrouter.ai/api/v1"
            - name: MULTIMODAL_EMBED_API_KEY
              value: "REDACTED"
            - name: MULTIMODAL_EMBED_MODEL
              value: "google/gemini-embedding-2-preview"
            - name: MULTIMODAL_EMBED_DIM
              value: "3072"
            - name: MULTIMODAL_EMBED_MAX_CONCURRENCY
              value: "3"
            - name: MULTIMODAL_VISUAL_COLLECTION
              value: "meai_rag_visual_v2"
            - name: MAX_PARALLEL_INSERT
              value: "4"
            - name: CHUNK_TOP_K
              value: "10"
            - name: RERANK_API_KEY
              value: "REDACTED"
            - name: RERANK_BASE_URL
              value: "https://api.jina.ai/v1/rerank"
            - name: RERANK_MODEL
              value: "jina-reranker-v2-base-multilingual"
            - name: VIDEORAG_ENABLED
              value: "1"
            - name: VIDEORAG_LLM_MODEL
              value: "openai/gpt-4o-mini"
            - name: VIDEORAG_AUDIO_MODEL
              value: "openai/whisper-1"
            - name: VIDEORAG_FRAMES_PER_SEGMENT
              value: "5"
            - name: VIDEORAG_SEGMENT_LENGTH_SECONDS
              value: "5"
            - name: VIDEORAG_SEGMENT_DIM
              value: "3072"
            - name: VIDEORAG_SEGMENT_TOP_K
              value: "4"
            - name: VIDEORAG_QDRANT_SEGMENT_COLLECTION
              value: "meai_rag_video_segments"
            - name: VIDEORAG_INSTANCE_CACHE_MAX
              value: "8"
            - name: VIDEORAG_MAX_DURATION_SECONDS
              value: "600"
            - name: VIDEORAG_WORKDIR_ROOT
              value: "/data/rag_storage/videorag"
            - name: VIDEORAG_S3_BUCKET
              value: "vkev2406-infra-khanghv2406v3-ap-southeast-1-terraform-state"
            - name: VIDEORAG_S3_REGION
              value: "ap-southeast-1"
            - name: VIDEORAG_S3_PUBLIC_BASE_URL
              value: "https://static.vkev.me"
            - name: VIDEORAG_S3_KEY_PREFIX
              value: "local/videorag-frames/"
            - name: VIDEORAG_S3_FRAME_TTL_SECONDS
              value: "604800"
            - name: VIDEORAG_HTTP_TIMEOUT
              value: "120"
            - name: AWS_ACCESS_KEY_ID
              value: "REDACTED"
            - name: AWS_SECRET_ACCESS_KEY
              value: "REDACTED"
            - name: HEALTH_PORT
              value: "8000"
            - name: GRPC_PORT
              value: "5006"
          volumeMounts:
            - name: rag-data
              mountPath: /data/rag_storage
          readinessProbe:
            httpGet:
              path: /health
              port: 8000
            initialDelaySeconds: 5
            periodSeconds: 15
            timeoutSeconds: 5
            failureThreshold: 12
          startupProbe:
            httpGet:
              path: /health
              port: 8000
            initialDelaySeconds: 10
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 90
          livenessProbe:
            httpGet:
              path: /health
              port: 8000
            initialDelaySeconds: 0
            periodSeconds: 30
            timeoutSeconds: 5
            failureThreshold: 6
          resources:
            requests:
              cpu: 500m
              memory: 1536Mi
            limits:
              cpu: 2000m
              memory: 3072Mi
      volumes:
        - name: rag-data
          persistentVolumeClaim:
            claimName: rag-data
---
apiVersion: v1
kind: Service
metadata:
  name: rag-microservice
  namespace: TERRAFORM_NAMESPACE
spec:
  selector:
    app: rag-microservice
  ports:
    - name: health
      port: 8000
      targetPort: 8000
    - name: grpc
      port: 5006
      targetPort: 5006
EOT

