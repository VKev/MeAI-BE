using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(EmailTemplateId), nameof(Language), IsUnique = true)]
public sealed class EmailTemplateContent
{
    [Key] public Guid Id { get; set; }

    public Guid EmailTemplateId { get; set; }

    [Required]
    public string Language { get; set; } = null!;

    [Required]
    public string Subject { get; set; } = null!;

    [Required]
    public string HtmlBody { get; set; } = null!;

    public string? TextBody { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamp")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? DeletedAt { get; set; }
}
