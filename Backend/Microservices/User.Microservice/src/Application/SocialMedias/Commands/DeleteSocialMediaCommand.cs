using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record DeleteSocialMediaCommand(Guid SocialMediaId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteSocialMediaCommandHandler
    : IRequestHandler<DeleteSocialMediaCommand, Result<bool>>
{
    private readonly IRepository<SocialMedia> _repository;

    public DeleteSocialMediaCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<bool>> Handle(DeleteSocialMediaCommand request, CancellationToken cancellationToken)
    {
        var socialMedia = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<bool>(new Error("SocialMedia.NotFound", "Social media not found"));
        }

        socialMedia.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        socialMedia.IsDeleted = true;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(socialMedia);

        return Result.Success(true);
    }
}
