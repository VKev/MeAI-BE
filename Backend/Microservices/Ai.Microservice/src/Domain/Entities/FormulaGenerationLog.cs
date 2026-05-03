using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class FormulaGenerationLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public Guid? FormulaTemplateId { get; set; }

    public string? FormulaKeySnapshot { get; set; }

    public string RenderedPrompt { get; set; } = null!;

    public string VariablesJson { get; set; } = null!;

    public string OutputType { get; set; } = null!;

    public string Model { get; set; } = null!;

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}
