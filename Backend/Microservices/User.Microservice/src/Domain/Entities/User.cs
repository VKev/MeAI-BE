namespace Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? FullName { get; set; }

    public DateTime? Birthday { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Provider { get; set; }

    public Guid? AvatarResourceId { get; set; }

    public string? Address { get; set; }

    public decimal? MeAiCoin { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();

    public ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
