using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class EmailTemplateContent
{
    [Key]
    public Guid Id { get; set; }

    public Guid EmailTemplateId { get; set; }

    public string Subject { get; set; } = null!;

    public string HtmlBody { get; set; } = null!;

    public string? TextBody { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
