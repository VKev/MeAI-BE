using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Formulas;

public interface IFormulaTemplateRenderer
{
    Result<FormulaTemplateRenderResult> Render(FormulaTemplateRenderRequest request);
}

public sealed record FormulaTemplateRenderRequest(
    string Template,
    IReadOnlyDictionary<string, string> Variables,
    string OutputType,
    string? Language,
    string? Instruction);

public sealed record FormulaTemplateRenderResult(
    string RenderedPrompt,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> MissingVariables);
