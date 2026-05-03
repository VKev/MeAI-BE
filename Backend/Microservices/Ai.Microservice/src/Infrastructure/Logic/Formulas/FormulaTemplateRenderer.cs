using System.Text;
using System.Text.RegularExpressions;
using Application.Abstractions.Formulas;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Formulas;

public sealed class FormulaTemplateRenderer : IFormulaTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{\s*(?<name>[a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Result<FormulaTemplateRenderResult> Render(FormulaTemplateRenderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Template))
        {
            return Result.Failure<FormulaTemplateRenderResult>(
                new Error("Formula.TemplateMissing", "Template is required."));
        }

        var normalizedOutputType = Normalize(request.OutputType);
        var normalizedVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in request.Variables)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalizedVariables[pair.Key.Trim()] = pair.Value ?? string.Empty;
        }

        var missingVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renderedTemplate = PlaceholderRegex.Replace(request.Template, match =>
        {
            var variableName = match.Groups["name"].Value;
            if (normalizedVariables.TryGetValue(variableName, out var value))
            {
                return value;
            }

            missingVariables.Add(variableName);
            return match.Value;
        });

        if (missingVariables.Count > 0)
        {
            var firstMissing = missingVariables.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).First();
            return Result.Failure<FormulaTemplateRenderResult>(
                new Error(
                    "Formula.MissingVariable",
                    $"Missing variable: {firstMissing}.",
                    new Dictionary<string, object?>
                    {
                        ["missingVariable"] = firstMissing,
                        ["missingVariables"] = missingVariables.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
                    }));
        }

        var unresolvedMatch = PlaceholderRegex.Match(renderedTemplate);
        if (unresolvedMatch.Success)
        {
            var unresolvedName = unresolvedMatch.Groups["name"].Value;
            return Result.Failure<FormulaTemplateRenderResult>(
                new Error(
                    "Formula.MissingVariable",
                    $"Missing variable: {unresolvedName}.",
                    new Dictionary<string, object?>
                    {
                        ["missingVariable"] = unresolvedName,
                        ["missingVariables"] = new[] { unresolvedName }
                    }));
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Output type: {normalizedOutputType}.");

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            builder.AppendLine($"Language: {request.Language.Trim()}.");
        }

        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            builder.AppendLine($"Instruction: {request.Instruction.Trim()}.");
        }

        builder.AppendLine();
        builder.AppendLine(renderedTemplate.Trim());

        return Result.Success(new FormulaTemplateRenderResult(
            builder.ToString().Trim(),
            normalizedVariables,
            Array.Empty<string>()));
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
