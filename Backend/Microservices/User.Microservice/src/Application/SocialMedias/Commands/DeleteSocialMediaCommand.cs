using Application.Abstractions.Data;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.SocialMedia;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record DeleteSocialMediaCommand(Guid SocialMediaId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteSocialMediaCommandHandler
    : IRequestHandler<DeleteSocialMediaCommand, Result<bool>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<SocialMedia> _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<DeleteSocialMediaCommandHandler> _logger;

    public DeleteSocialMediaCommandHandler(
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ILogger<DeleteSocialMediaCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _repository = unitOfWork.Repository<SocialMedia>();
        _publishEndpoint = publishEndpoint;
        _logger = logger;
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _publishEndpoint.Publish(
                new SocialMediaUnlinked
                {
                    CorrelationId = Guid.CreateVersion7(),
                    UserId = request.UserId,
                    SocialMediaId = socialMedia.Id,
                    Platform = socialMedia.Type,
                    RequestedAt = DateTimeExtensions.PostgreSqlUtcNow
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to queue social media unlink cleanup. UserId: {UserId}, SocialMediaId: {SocialMediaId}, Platform: {Platform}",
                request.UserId,
                socialMedia.Id,
                socialMedia.Type);
        }

        return Result.Success(true);
    }
}
