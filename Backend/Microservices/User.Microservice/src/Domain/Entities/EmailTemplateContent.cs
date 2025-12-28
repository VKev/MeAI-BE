using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(EmailTemplateId), IsUnique = true)]
public sealed class EmailTemplateContent
{
    [Key] public Guid Id { get; set; }

    public Guid EmailTemplateId { get; set; }

    [Required]
    public string Subject { get; set; } = null!;

    [Required]
    public string HtmlBody { get; set; } = null!;

    public string? TextBody { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamptz")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
