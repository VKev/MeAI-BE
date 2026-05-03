using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PromptFormulaTemplate
{
    [Key]
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Template { get; set; } = null!;

    public string OutputType { get; set; } = null!;

    public string? DefaultLanguage { get; set; }

    public string? DefaultInstruction { get; set; }

    public bool IsActive { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
