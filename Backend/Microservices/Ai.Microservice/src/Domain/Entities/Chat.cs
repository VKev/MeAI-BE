using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Chat
{
    [Key]
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public string? Prompt { get; set; }

    [Column(TypeName = "json")]
    public string? Config { get; set; }

    [Column(TypeName = "json")]
    public string? ReferenceResourceIds { get; set; }

    [Column(TypeName = "json")]
    public string? ResultResourceIds { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
