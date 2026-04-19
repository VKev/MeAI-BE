using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Follow
{
    [Key]
    public Guid Id { get; set; }

    public Guid FollowerId { get; set; }

    public Guid FolloweeId { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }
}
