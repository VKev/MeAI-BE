using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Subscription
{
    [Key] public Guid Id { get; set; }

    public string? Name { get; set; }

    public int? NumberOfSocialAccounts { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal? MeAiCoin { get; set; }

    public int? RateLimitForContentCreation { get; set; }

    public int? NumberOfWorkspaces { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamptz")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
