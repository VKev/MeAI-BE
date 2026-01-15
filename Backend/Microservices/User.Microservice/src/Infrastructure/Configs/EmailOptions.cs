namespace Infrastructure.Configs;

public sealed class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseStartTls { get; set; } = true;
    public bool UseSsl { get; set; } = false;
    public bool DisableCertificateRevocationCheck { get; set; } = false;
    public string FromEmail { get; set; } = string.Empty;
    public string? FromName { get; set; }
}
