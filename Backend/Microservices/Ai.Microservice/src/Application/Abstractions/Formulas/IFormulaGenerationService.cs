using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Formulas;

public interface IFormulaGenerationService
{
    Task<Result<FormulaGenerationServiceResult>> GenerateAsync(
        FormulaGenerationServiceRequest request,
        CancellationToken cancellationToken);
}

public sealed record FormulaGenerationServiceRequest(
    string RenderedPrompt,
    string OutputType,
    int VariantCount,
    string? PreferredModel = null);

public sealed record FormulaGenerationServiceResult(
    string Model,
    IReadOnlyList<string> Outputs);
