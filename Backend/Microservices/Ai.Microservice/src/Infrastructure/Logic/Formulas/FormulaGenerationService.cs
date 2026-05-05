using System.Text;
using Application.Abstractions.Formulas;
using Application.Abstractions.Gemini;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Formulas;

public sealed class FormulaGenerationService : IFormulaGenerationService
{
    private const string DefaultModel = "gpt-5-4";

    private readonly IGeminiCaptionService _captionService;

    public FormulaGenerationService(IGeminiCaptionService captionService)
    {
        _captionService = captionService;
    }

    public async Task<Result<FormulaGenerationServiceResult>> GenerateAsync(
        FormulaGenerationServiceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RenderedPrompt))
        {
            return Result.Failure<FormulaGenerationServiceResult>(
                new Error("Formula.TemplateMissing", "Rendered prompt is required."));
        }

        var variantCount = Math.Clamp(request.VariantCount, 1, 5);
        var model = string.IsNullOrWhiteSpace(request.PreferredModel)
            ? DefaultModel
            : request.PreferredModel.Trim();

        var prompt = BuildPrompt(request.RenderedPrompt, request.OutputType, variantCount);
        var outputs = new List<string>(variantCount);

        for (var index = 0; index < variantCount; index++)
        {
            var result = await _captionService.GenerateTitleAsync(
                new GeminiTitleRequest(prompt, null, model),
                cancellationToken);

            if (result.IsFailure)
            {
                return Result.Failure<FormulaGenerationServiceResult>(result.Error);
            }

            outputs.Add(result.Value.Trim());
        }

        return Result.Success(new FormulaGenerationServiceResult(model, outputs));
    }

    private static string BuildPrompt(string renderedPrompt, string outputType, int variantCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"You are generating {variantCount} {outputType} output(s).");
        builder.AppendLine("Return only the requested text for the current variant. No numbering. No markdown.");
        builder.AppendLine();
        builder.Append(renderedPrompt.Trim());
        return builder.ToString();
    }
}
