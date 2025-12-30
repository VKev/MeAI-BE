namespace Infrastructure.Configuration;

public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Email { get; set; }

    public string? FullName { get; set; }

    public string RoleName { get; set; } = "Admin";
}
