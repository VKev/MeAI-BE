using Application.Abstractions.Resources;
using Application.Posts.Commands;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class DeletePostCommandTests
{
    [Fact]
    public async Task Handle_Should_DeleteAttachedResources_WhenDeletingOwnedPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "Feed post",
            ResourceIds = new[] { resourceId },
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        userResourceService
            .Setup(service => service.DeleteResourcesAsync(
                userId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(resourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(1));

        var handler = new DeletePostCommandHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new DeletePostCommand(userId, post.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        userResourceService.VerifyAll();
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }
}
