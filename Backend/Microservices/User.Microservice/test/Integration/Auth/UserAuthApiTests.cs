using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Users.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebApi.Controllers;

namespace test.Integration.Auth;

public sealed class UserAuthApiTests(UserAuthApiFixture fixture) : IClassFixture<UserAuthApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Register_WithSeededVerificationCode_ReturnsLoginResult_And_SetsCookies()
    {
        using var client = fixture.CreateClient();
        var registration = CreateRegistration();

        await fixture.SeedEmailVerificationCodeAsync(registration.Email, registration.Code);

        var response = await client.PostAsJsonAsync("/api/User/auth/register", registration.Request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        setCookieHeaders.Should().NotBeNull();
        setCookieHeaders!.Should().Contain(header => header.Contains("access_token=", StringComparison.OrdinalIgnoreCase));
        setCookieHeaders.Should().Contain(header => header.Contains("refresh_token=", StringComparison.OrdinalIgnoreCase));

        var payload = await ReadJsonAsync<ApiResultContract<LoginResponse>>(response);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Email.Should().Be(registration.Email.ToLowerInvariant());
        payload.Value.Roles.Should().Contain("USER");
        payload.Value.AccessToken.Should().NotBeNullOrWhiteSpace();
        payload.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_Then_Me_ReturnsExpectedUserProfile()
    {
        using var client = fixture.CreateClient();
        var registration = CreateRegistration();

        await RegisterAsync(client, registration);
        await LogoutAsync(client);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/User/auth/login",
            new LoginRequest(registration.Email, registration.Password));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginPayload = await ReadJsonAsync<ApiResultContract<LoginResponse>>(loginResponse);
        loginPayload.IsSuccess.Should().BeTrue();

        var meResponse = await client.GetAsync("/api/User/auth/me");

        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var mePayload = await ReadJsonAsync<ApiResultContract<UserProfileResponse>>(meResponse);
        mePayload.IsSuccess.Should().BeTrue();
        mePayload.Value.Email.Should().Be(registration.Email.ToLowerInvariant());
        mePayload.Value.Username.Should().Be(registration.Username);
        mePayload.Value.Roles.Should().Contain("USER");
        mePayload.Value.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_WithRefreshCookie_RotatesTokens_And_RevokesThePreviousRefreshToken()
    {
        using var client = fixture.CreateClient();
        var registration = CreateRegistration();

        var registerPayload = await RegisterAsync(client, registration);
        var originalRefreshToken = registerPayload.Value.RefreshToken;

        var refreshResponse = await client.PostAsync("/api/User/auth/refresh", content: null);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshPayload = await ReadJsonAsync<ApiResultContract<LoginResponse>>(refreshResponse);
        refreshPayload.IsSuccess.Should().BeTrue();
        refreshPayload.Value.RefreshToken.Should().NotBe(originalRefreshToken);

        var refreshTokenState = await fixture.ExecuteDbContextAsync(async dbContext =>
        {
            var oldTokenHash = UserAuthApiFixture.HashRefreshToken(originalRefreshToken);
            var newTokenHash = UserAuthApiFixture.HashRefreshToken(refreshPayload.Value.RefreshToken);

            var oldToken = await dbContext.RefreshTokens
                .AsNoTracking()
                .SingleAsync(token => token.TokenHash == oldTokenHash);

            var newToken = await dbContext.RefreshTokens
                .AsNoTracking()
                .SingleAsync(token => token.TokenHash == newTokenHash);

            return new
            {
                OldTokenRevokedAt = oldToken.RevokedAt,
                OldTokenAccessRevokedAt = oldToken.AccessTokenRevokedAt,
                NewTokenRevokedAt = newToken.RevokedAt
            };
        });

        refreshTokenState.OldTokenRevokedAt.Should().NotBeNull();
        refreshTokenState.OldTokenAccessRevokedAt.Should().NotBeNull();
        refreshTokenState.NewTokenRevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Logout_ClearsCookies_And_Me_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();
        var registration = CreateRegistration();

        await RegisterAsync(client, registration);

        var logoutResponse = await client.PostAsync("/api/User/auth/logout", content: null);

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        logoutResponse.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        setCookieHeaders.Should().NotBeNull();
        setCookieHeaders!.Should().Contain(header => header.Contains("access_token=;", StringComparison.OrdinalIgnoreCase));
        setCookieHeaders.Should().Contain(header => header.Contains("refresh_token=;", StringComparison.OrdinalIgnoreCase));

        var meResponse = await client.GetAsync("/api/User/auth/me");

        meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var message = await ReadJsonAsync<MessageResponse>(meResponse);
        message.Message.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorizedMessage()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsync("/api/User/auth/refresh", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var message = await ReadJsonAsync<MessageResponse>(response);
        message.Message.Should().Be("Missing refresh token");
    }

    [Fact]
    public async Task Register_WithWrongVerificationCode_ReturnsProblemDetails()
    {
        using var client = fixture.CreateClient();
        var registration = CreateRegistration();

        await fixture.SeedEmailVerificationCodeAsync(registration.Email, "123456");

        var response = await client.PostAsJsonAsync(
            "/api/User/auth/register",
            registration.Request with { Code = "654321" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        document.RootElement.GetProperty("type").GetString().Should().Be("Auth.InvalidVerificationCode");
        document.RootElement.GetProperty("detail").GetString().Should().Be("Invalid or expired code");
    }

    private static RegistrationData CreateRegistration()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var username = $"user{suffix}";
        var email = $"{username}@example.test";
        const string password = "Passw0rd!";
        const string code = "123456";

        return new RegistrationData(
            username,
            email,
            password,
            code,
            new RegisterRequest(
                username,
                email,
                password,
                code,
                $"Test {suffix}",
                "0123456789"));
    }

    private async Task<ApiResultContract<LoginResponse>> RegisterAsync(HttpClient client, RegistrationData registration)
    {
        await fixture.SeedEmailVerificationCodeAsync(registration.Email, registration.Code);

        var response = await client.PostAsJsonAsync("/api/User/auth/register", registration.Request);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<ApiResultContract<LoginResponse>>(response);
    }

    private static async Task LogoutAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/User/auth/logout", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        value.Should().NotBeNull();
        return value!;
    }

    private sealed record RegistrationData(
        string Username,
        string Email,
        string Password,
        string Code,
        RegisterRequest Request);
}
