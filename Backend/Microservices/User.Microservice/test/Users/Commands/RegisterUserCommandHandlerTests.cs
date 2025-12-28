using System.Linq.Expressions;
using Application.Users.Commands.Register;
using Application.Abstractions.Security;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;

namespace test.Users.Commands
{
    public class RegisterUserCommandHandlerTests
    {
        private readonly Mock<IRepository<User>> _userRepositoryMock;
        private readonly Mock<IRepository<Role>> _roleRepositoryMock;
        private readonly Mock<IRepository<UserRole>> _userRoleRepositoryMock;
        private readonly Mock<IRepository<RefreshToken>> _refreshTokenRepositoryMock;
        private readonly Mock<IPasswordHasher> _passwordHasherMock;
        private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
        private readonly Mock<IEmailRepository> _emailRepositoryMock;
        private readonly Mock<IVerificationCodeStore> _verificationCodeStoreMock;
        private readonly IConfiguration _configuration;

        public RegisterUserCommandHandlerTests()
        {
            _userRepositoryMock = new();
            _roleRepositoryMock = new();
            _userRoleRepositoryMock = new();
            _refreshTokenRepositoryMock = new();
            _passwordHasherMock = new();
            _jwtTokenServiceMock = new();
            _emailRepositoryMock = new();
            _verificationCodeStoreMock = new();
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Jwt:ExpirationMinutes", "60" }
                })
                .Build();
            _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed");
            _jwtTokenServiceMock.Setup(j => j.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<List<string>>()))
                .Returns("access-token");
            _jwtTokenServiceMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh-token");
            _emailRepositoryMock.Setup(e => e.SendEmailAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    default))
                .Returns(Task.CompletedTask);
            _verificationCodeStoreMock.Setup(v => v.StoreAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    default))
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task Handle_Should_ReturnSuccessResult_When_UserNotExist()
        {
            var command = new RegisterUserCommand("test_user", "test_user_email", "test_password");
            _userRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<User, bool>>>(), default))
                .ReturnsAsync(Array.Empty<User>());
            _roleRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Role, bool>>>(), default))
                .ReturnsAsync(new[] { new Role { Id = Guid.NewGuid(), Name = "USER" } });
            _refreshTokenRepositoryMock
                .Setup(r => r.FindAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), default))
                .ReturnsAsync(Array.Empty<RefreshToken>());

            var handler = new RegisterUserCommandHandler(
                _userRepositoryMock.Object,
                _roleRepositoryMock.Object,
                _userRoleRepositoryMock.Object,
                _refreshTokenRepositoryMock.Object,
                _passwordHasherMock.Object,
                _jwtTokenServiceMock.Object,
                _configuration,
                _emailRepositoryMock.Object,
                _verificationCodeStoreMock.Object);

            Result<LoginResponse> result = await handler.Handle(command, default);
            result.IsSuccess.Should().BeTrue();
            result.Value.AccessToken.Should().Be("access-token");
        }
    }
}
