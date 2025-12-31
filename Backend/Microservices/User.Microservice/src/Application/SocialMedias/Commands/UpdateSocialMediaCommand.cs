using System.Text.Json;
using Application.Abstractions.Data;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record UpdateSocialMediaCommand(
    Guid SocialMediaId,
    Guid UserId,
    string Type,
    JsonDocument? Metadata) : IRequest<Result<SocialMediaResponse>>;

public sealed class UpdateSocialMediaCommandHandler
    : IRequestHandler<UpdateSocialMediaCommand, Result<SocialMediaResponse>>
{
    private readonly IRepository<SocialMedia> _repository;

    public UpdateSocialMediaCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<SocialMediaResponse>> Handle(UpdateSocialMediaCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedia = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("SocialMedia.NotFound", "Social media not found"));
        }

        socialMedia.Type = request.Type.Trim();
        socialMedia.Metadata = request.Metadata;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _repository.Update(socialMedia);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
