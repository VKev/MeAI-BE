using System.Linq.Expressions;
using Application.Users.Commands.Register;
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
        private readonly IConfiguration _configuration;

        public RegisterUserCommandHandlerTests()
        {
            _userRepositoryMock = new();
            _roleRepositoryMock = new();
            _userRoleRepositoryMock = new();
            _refreshTokenRepositoryMock = new();
            _passwordHasherMock = new();
            _jwtTokenServiceMock = new();
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
                _configuration);

            Result<LoginResponse> result = await handler.Handle(command, default);
            result.IsSuccess.Should().BeTrue();
            result.Value.AccessToken.Should().Be("access-token");
        }
    }
}
