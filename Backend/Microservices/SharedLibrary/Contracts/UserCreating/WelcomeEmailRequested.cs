namespace SharedLibrary.Contracts.UserCreating;

public sealed record WelcomeEmailRequested(
    Guid UserId,
    string Email,
    string? FullName,
    string? Username);
