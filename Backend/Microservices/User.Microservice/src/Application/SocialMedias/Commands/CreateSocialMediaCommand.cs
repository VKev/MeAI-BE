using System.Text.Json;
using Application.Abstractions.Data;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CreateSocialMediaCommand(
    Guid UserId,
    string Type,
    JsonDocument? Metadata) : IRequest<Result<SocialMediaResponse>>;

public sealed class CreateSocialMediaCommandHandler
    : IRequestHandler<CreateSocialMediaCommand, Result<SocialMediaResponse>>
{
    private readonly IRepository<SocialMedia> _repository;

    public CreateSocialMediaCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<SocialMediaResponse>> Handle(CreateSocialMediaCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedia = new SocialMedia
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Type = request.Type.Trim(),
            Metadata = request.Metadata,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(socialMedia, cancellationToken);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
