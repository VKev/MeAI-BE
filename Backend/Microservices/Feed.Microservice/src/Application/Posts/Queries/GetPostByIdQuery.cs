using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetPostByIdQuery(Guid PostId, Guid? RequestingUserId) : IQuery<PostResponse>;

public sealed class GetPostByIdQueryHandler : IQueryHandler<GetPostByIdQuery, PostResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetPostByIdQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<PostResponse>> Handle(GetPostByIdQuery request, CancellationToken cancellationToken)
    {
        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (post is null)
        {
            return Result.Failure<PostResponse>(FeedErrors.PostNotFound);
        }

        var response = await FeedPostSupport.ToPostResponseAsync(
            _unitOfWork,
            _userResourceService,
            request.RequestingUserId,
            post,
            cancellationToken);
        return Result.Success(response);
    }
}
