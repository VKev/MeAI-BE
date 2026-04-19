using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PostHashtag
{
    [Key]
    public Guid Id { get; set; }

    public Guid PostId { get; set; }

    public Guid HashtagId { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }
}
