namespace Infrastructure.Configuration;

public sealed class DefaultUserSeedOptions
{
    public const string SectionName = "DefaultUser";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Email { get; set; }

    public string? FullName { get; set; }

    public string RoleName { get; set; } = "USER";
}
