namespace Application.Formulas.Models;

public sealed record FormulaGenerateResponse(
    Guid? FormulaId,
    string? FormulaKey,
    string OutputType,
    string RenderedPrompt,
    string Model,
    IReadOnlyList<string> Outputs,
    Guid UsageReferenceId);
