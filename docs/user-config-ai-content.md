# User Config For AI Content

## Muc dich

Tai lieu nay mo ta cach `Config` trong database cua `User.Microservice` duoc su dung khi tao AI content trong `Ai.Microservice`.

`Config` nay la business config dung chung, khong phai config ha tang nhu JWT, database, Redis, Stripe, hay appsettings.

## Nguon du lieu

Bang `configs` duoc map boi:
- `Backend/Microservices/User.Microservice/src/Domain/Entities/Config.cs`
- `Backend/Microservices/User.Microservice/src/Infrastructure/Context/Configuration/ConfigConfiguration.cs`

Field hien co:
- `ChatModel`
- `MediaAspectRatio`
- `NumberOfVariances`
- `CreatedAt`
- `UpdatedAt`
- `DeletedAt`
- `IsDeleted`

`User.Microservice` luon lay active config theo ban ghi moi nhat, chua bi xoa mem.

## Cach Ai.Microservice lay config

`Ai.Microservice` khong doc truc tiep database cua `User.Microservice`.
No lay config qua gRPC de giu dung Clean Architecture va giao tiep service-to-service dong bo.

### gRPC contract

Proto duoc mo rong tai:
- `Backend/Microservices/SharedLibrary/Protos/user_resources.proto`

Da them RPC:
- `GetActiveConfig`

### User service gRPC server

`User.Microservice` expose active config tai:
- `Backend/Microservices/User.Microservice/src/WebApi/Grpc/UserResourceGrpcService.cs`

Neu khong co config active:
- service tra ve `HasActiveConfig = false`

Neu co config:
- service tra ve `ConfigId`
- `ChatModel`
- `MediaAspectRatio`
- `NumberOfVariances`

### Ai service gRPC client

`Ai.Microservice` dung abstraction:
- `Backend/Microservices/Ai.Microservice/src/Application/Abstractions/Configs/IUserConfigService.cs`

Implementation:
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Configs/UserConfigGrpcService.cs`

Dang ky DI:
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/DependencyInjection.cs`

## Anh huong hien tai den AI content

### 1. Tao image AI

File:
- `Backend/Microservices/Ai.Microservice/src/Application/Chats/Commands/CreateChatImageCommand.cs`

Hanh vi:
- Neu request co `AspectRatio` thi uu tien gia tri request.
- Neu request khong co `AspectRatio`, service se lay `MediaAspectRatio` tu active config.
- Neu database config cung khong co, fallback ve `"1:1"`.

Ket qua:
- image generation se dung ti le anh mac dinh tu database config cua user service.

### 2. Tao video AI

File:
- `Backend/Microservices/Ai.Microservice/src/Application/Chats/Commands/CreateChatVideoCommand.cs`

Hanh vi:
- `Model`
  - Neu request co `Model` thi uu tien gia tri request.
  - Neu request khong co `Model`, service se lay `ChatModel` tu active config.
  - Neu config cung khong co, fallback ve `"veo3_fast"`.
- `AspectRatio`
  - Neu request co `AspectRatio` thi uu tien gia tri request.
  - Neu request khong co `AspectRatio`, service se lay `MediaAspectRatio` tu active config.
  - Neu config cung khong co, fallback ve `"16:9"`.

Ket qua:
- video generation co the dung model mac dinh va aspect ratio mac dinh duoc quan ly tu database config.

### 3. Tao caption/title bang Gemini

File:
- `Backend/Microservices/Ai.Microservice/src/Application/Posts/Commands/CreateGeminiPostCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Abstractions/Gemini/IGeminiCaptionService.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Gemini/GeminiCaptionService.cs`

Hanh vi:
- Neu active config co `ChatModel`, gia tri nay duoc truyen xuong Gemini caption generation va title generation nhu `PreferredModel`.
- Neu khong co `ChatModel`, service dung model Gemini mac dinh trong app config.

Ket qua:
- caption/title AI co the dong bo theo model uu tien duoc set trong database config.

## Thu tu uu tien gia tri

Cho cac truong dang duoc ap dung, thu tu uu tien la:

1. Gia tri user gui trong request
2. Gia tri active config trong database cua `User.Microservice`
3. Gia tri fallback hardcoded trong `Ai.Microservice`

Dieu nay giu cho:
- frontend van co the override theo tung request
- admin van co the dat default chung trong database
- he thong van co fallback an toan neu chua co config

## NumberOfVariances hien tai

`NumberOfVariances` da duoc expose qua gRPC contract, nhung chua duoc ap dung vao image/video generation flow.

Ly do:
- provider va message contract hien tai chua co duong day ro rang de yeu cau tao nhieu bien the trong mot lan generate
- neu bat gia tri nay ngay luc nay se de den config co mat trong DB nhung khong thay doi ket qua thuc te

Trang thai hien tai:
- da san sang de doc tu `User.Microservice`
- chua duoc dung trong runtime behavior

## Muon dung NumberOfVariances thi can lam gi

Can mo rong them cac lop sau:
- request/command tao image hoac video
- event contract message bus neu can
- provider integration (`KieImageService`, `VeoVideoService`) neu provider ho tro nhieu outputs
- callback/result mapping de luu va tra ve nhieu ket qua

Neu provider khong ho tro native multiple outputs:
- can dinh nghia ro co chap nhan fan-out thanh nhieu task rieng hay khong
- neu co, can xu ly correlation, luu trang thai, chi phi, va response format

## Cac file chinh lien quan

- `Backend/Microservices/SharedLibrary/Protos/user_resources.proto`
- `Backend/Microservices/User.Microservice/src/WebApi/Grpc/UserResourceGrpcService.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Abstractions/Configs/IUserConfigService.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Configs/UserConfigGrpcService.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/DependencyInjection.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Chats/Commands/CreateChatImageCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Chats/Commands/CreateChatVideoCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Posts/Commands/CreateGeminiPostCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Gemini/GeminiCaptionService.cs`

## Ghi chu van hanh

De thay doi default cho AI content, admin co the goi:
- `GET /api/User/admin/config`
- `PUT /api/User/admin/config`

Khi update config:
- request moi tao AI content sau do se nhan gia tri moi
- request da tao truoc do khong bi sua nguoc
