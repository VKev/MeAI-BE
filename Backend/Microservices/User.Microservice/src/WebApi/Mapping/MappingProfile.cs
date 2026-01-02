using System.Text.Json;
using Application.Configs.Commands;
using Application.Resources.Commands;
using Application.SocialMedias.Commands;
using Application.Subscriptions.Commands;
using Application.Users.Commands;
using Application.Workspaces.Commands;
using Application.WorkspaceSocialMedias.Commands;
using AutoMapper;
using WebApi.Controllers;

namespace WebApi.Mapping;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<LoginRequest, LoginWithPasswordCommand>();
        CreateMap<RegisterRequest, RegisterUserCommand>();
        CreateMap<GoogleLoginRequest, LoginWithGoogleCommand>();
        CreateMap<ForgotPasswordRequest, ForgotPasswordCommand>();
        CreateMap<ResetPasswordRequest, ResetPasswordCommand>();
        CreateMap<SendVerificationCodeRequest, SendEmailVerificationCodeCommand>();
        CreateMap<VerifyEmailRequest, VerifyEmailCommand>();
        CreateMap<CreateSubscriptionRequest, CreateSubscriptionCommand>();
        CreateMap<PurchaseSubscriptionRequest, PurchaseSubscriptionCommand>()
            .ForCtorParam("SubscriptionId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<UpdateSubscriptionRequest, UpdateSubscriptionCommand>()
            .ForCtorParam("Id", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<PatchSubscriptionRequest, PatchSubscriptionCommand>()
            .ForCtorParam("Id", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<UpdateConfigRequest, UpdateConfigCommand>();
        CreateMap<CreateAdminUserRequest, CreateUserCommand>();
        CreateMap<UpdateAdminUserRequest, UpdateUserCommand>()
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<UpdateUserRoleRequest, SetUserRoleCommand>()
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<CreateResourceRequest, CreateResourceCommand>()
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<UpdateResourceRequest, UpdateResourceCommand>()
            .ForCtorParam("ResourceId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<CreateWorkspaceRequest, CreateWorkspaceCommand>()
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<UpdateWorkspaceRequest, UpdateWorkspaceCommand>()
            .ForCtorParam("WorkspaceId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty));
        CreateMap<CreateSocialMediaRequest, CreateSocialMediaCommand>()
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("Metadata", opt => opt.MapFrom(_ => (JsonDocument?)null));
        CreateMap<UpdateSocialMediaRequest, UpdateSocialMediaCommand>()
            .ForCtorParam("SocialMediaId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("Metadata", opt => opt.MapFrom(_ => (JsonDocument?)null));
        CreateMap<CreateWorkspaceSocialMediaRequest, CreateWorkspaceSocialMediaCommand>()
            .ForCtorParam("WorkspaceId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("Metadata", opt => opt.MapFrom(_ => (JsonDocument?)null));
        CreateMap<UpdateWorkspaceSocialMediaRequest, UpdateWorkspaceSocialMediaCommand>()
            .ForCtorParam("WorkspaceId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("SocialMediaId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("UserId", opt => opt.MapFrom(_ => Guid.Empty))
            .ForCtorParam("Metadata", opt => opt.MapFrom(_ => (JsonDocument?)null));
    }
}
