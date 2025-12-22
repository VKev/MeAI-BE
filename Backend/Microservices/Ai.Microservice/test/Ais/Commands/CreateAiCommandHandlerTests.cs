using System;
using SharedLibrary.Common.ResponseModel;
using Application.Ais.Commands;
using Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Moq;

namespace test.Ais.Commands
{
    public class CreateAiCommandHandlerTests
    {
        private readonly Mock<IAiRepository> _aiRepositoryMock;
        private readonly Mock<IPublishEndpoint> _publishEndpointMock;

        public CreateAiCommandHandlerTests()
        {
            _aiRepositoryMock = new();
            _publishEndpointMock = new();
        }

        [Fact]
        public async Task Handle_Should_ReturnSuccessResult_When_UserNotExist()
        {
            var command = new CreateAiCommand("test_user", "test_user_email");
            var handler = new CreateAiCommandHandler(_aiRepositoryMock.Object, _publishEndpointMock.Object);
            Result result = await handler.Handle(command,default);
            result.IsSuccess.Should().BeTrue();
        }
    }
}
