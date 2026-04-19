using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Hashtag
{
    [Key]
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public int PostCount { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }
}
