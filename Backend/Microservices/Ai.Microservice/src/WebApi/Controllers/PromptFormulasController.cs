using System.Security.Claims;
using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Formulas;
using Application.Billing;
using Application.Formulas;
using Application.Formulas.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai")]
public sealed class PromptFormulasController : ApiController
{
    private const string DefaultModel = "gpt-5-4";

    private static readonly JsonSerializerOptions VariableJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPromptFormulaTemplateRepository _templateRepository;
    private readonly IFormulaTemplateRenderer _renderer;
    private readonly IFormulaGenerationService _generationService;
    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;
    private readonly IFormulaGenerationLogRepository _formulaGenerationLogRepository;

    public PromptFormulasController(
        IMediator mediator,
        IPromptFormulaTemplateRepository templateRepository,
        IFormulaTemplateRenderer renderer,
        IFormulaGenerationService generationService,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IAiSpendRecordRepository aiSpendRecordRepository,
        IFormulaGenerationLogRepository formulaGenerationLogRepository) : base(mediator)
    {
        _templateRepository = templateRepository;
        _renderer = renderer;
        _generationService = generationService;
        _pricingService = pricingService;
        _billingClient = billingClient;
        _aiSpendRecordRepository = aiSpendRecordRepository;
        _formulaGenerationLogRepository = formulaGenerationLogRepository;
    }

    [HttpPost("formulas/generate")]
    [Authorize]
    [ProducesResponseType(typeof(Result<FormulaGenerateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate(
        [FromBody] FormulaGenerateRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        if (request is null)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(
                new Error("Formula.InvalidRequest", "Request body is required.")));
        }

        var outputTypeResult = NormalizeOutputType(request.OutputType);
        if (outputTypeResult.IsFailure)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(outputTypeResult.Error));
        }

        var templateSourceResult = await ResolveTemplateSourceAsync(request, cancellationToken);
        if (templateSourceResult.IsFailure)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(templateSourceResult.Error));
        }

        var templateSource = templateSourceResult.Value;
        if (templateSource.TemplateEntity is not null &&
            !string.Equals(templateSource.TemplateEntity.OutputType, outputTypeResult.Value, StringComparison.OrdinalIgnoreCase))
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(
                new Error(
                    "Formula.InvalidOutputType",
                    "outputType does not match the selected formula template.",
                    new Dictionary<string, object?>
                    {
                        ["outputType"] = outputTypeResult.Value,
                        ["formulaOutputType"] = templateSource.TemplateEntity.OutputType
                    })));
        }

        var variablesResult = NormalizeVariables(request.Variables);
        if (variablesResult.IsFailure)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(variablesResult.Error));
        }

        var renderResult = _renderer.Render(new FormulaTemplateRenderRequest(
            templateSource.Template,
            variablesResult.Value,
            outputTypeResult.Value,
            string.IsNullOrWhiteSpace(request.Language) ? templateSource.DefaultLanguage : request.Language?.Trim(),
            string.IsNullOrWhiteSpace(request.Instruction) ? templateSource.DefaultInstruction : request.Instruction?.Trim()));

        if (renderResult.IsFailure)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(renderResult.Error));
        }

        var variantCount = request.VariantCount.GetValueOrDefault(1);
        if (variantCount <= 0 || variantCount > 5)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(
                new Error("Formula.InvalidVariantCount", "variantCount must be between 1 and 5.")));
        }

        var quoteResult = await _pricingService.GetCostAsync(
            CoinActionTypes.FormulaGeneration,
            DefaultModel,
            variant: null,
            quantity: variantCount,
            cancellationToken);

        if (quoteResult.IsFailure)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(quoteResult.Error));
        }

        var referenceId = Guid.CreateVersion7().ToString();
        var debitResult = await _billingClient.DebitAsync(
            userId,
            quoteResult.Value.TotalCoins,
            CoinDebitReasons.FormulaGenerationDebit,
            CoinReferenceTypes.FormulaGeneration,
            referenceId,
            cancellationToken);

        if (debitResult.IsFailure)
        {
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(debitResult.Error));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var workspaceId = NormalizeGuid(request.WorkspaceId);
        var spendRecord = new AiSpendRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = workspaceId,
            Provider = AiSpendProviders.Kie,
            ActionType = CoinActionTypes.FormulaGeneration,
            Model = DefaultModel,
            Variant = null,
            Unit = quoteResult.Value.Unit,
            Quantity = quoteResult.Value.Quantity,
            UnitCostCoins = quoteResult.Value.UnitCostCoins,
            TotalCoins = quoteResult.Value.TotalCoins,
            ReferenceType = CoinReferenceTypes.FormulaGeneration,
            ReferenceId = referenceId,
            Status = AiSpendStatuses.Debited,
            CreatedAt = now
        };

        await _aiSpendRecordRepository.AddAsync(spendRecord, cancellationToken);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);

        var generationResult = await _generationService.GenerateAsync(
            new FormulaGenerationServiceRequest(
                renderResult.Value.RenderedPrompt,
                outputTypeResult.Value,
                variantCount),
            cancellationToken);

        if (generationResult.IsFailure)
        {
            await RefundAsync(userId, quoteResult.Value.TotalCoins, referenceId, spendRecord, cancellationToken);
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(generationResult.Error));
        }

        var outputs = generationResult.Value.Outputs
            .Where(output => !string.IsNullOrWhiteSpace(output))
            .Select(output => output.Trim())
            .ToList();

        if (outputs.Count == 0)
        {
            await RefundAsync(userId, quoteResult.Value.TotalCoins, referenceId, spendRecord, cancellationToken);
            return HandleFailure(Result.Failure<FormulaGenerateResponse>(
                new Error("Formula.EmptyOutput", "AI did not return any output.")));
        }

        var log = new FormulaGenerationLog
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = workspaceId,
            FormulaTemplateId = templateSource.TemplateEntity?.Id,
            FormulaKeySnapshot = templateSource.FormulaKey,
            RenderedPrompt = renderResult.Value.RenderedPrompt,
            VariablesJson = JsonSerializer.Serialize(renderResult.Value.Variables, VariableJsonOptions),
            OutputType = outputTypeResult.Value,
            Model = generationResult.Value.Model,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _formulaGenerationLogRepository.AddAsync(log, cancellationToken);
        await _formulaGenerationLogRepository.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(new FormulaGenerateResponse(
            templateSource.TemplateEntity?.Id,
            templateSource.FormulaKey,
            outputTypeResult.Value,
            renderResult.Value.RenderedPrompt,
            generationResult.Value.Model,
            outputs,
            spendRecord.Id)));
    }

    [HttpGet("admin/formulas")]
    [Authorize("ADMIN", "Admin", "admin")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PromptFormulaTemplateResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _templateRepository.GetAllAsync(cancellationToken);
        return Ok(Result.Success<IReadOnlyList<PromptFormulaTemplateResponse>>(
            items.Select(MapToResponse).ToList()));
    }

    [HttpPost("admin/formulas")]
    [Authorize("ADMIN", "Admin", "admin")]
    [ProducesResponseType(typeof(Result<PromptFormulaTemplateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertPromptFormulaTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = ValidateAdminRequest(request);
        if (validationResult.IsFailure)
        {
            return HandleFailure(Result.Failure<PromptFormulaTemplateResponse>(validationResult.Error));
        }

        var existing = await _templateRepository.GetByKeyAsync(request.Key.Trim(), cancellationToken);
        if (existing is not null)
        {
            return HandleFailure(Result.Failure<PromptFormulaTemplateResponse>(
                new Error("Formula.AlreadyExists", "Formula key already exists.")));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var entity = new PromptFormulaTemplate
        {
            Id = Guid.CreateVersion7(),
            Key = request.Key.Trim(),
            Name = request.Name.Trim(),
            Template = request.Template.Trim(),
            OutputType = request.OutputType.Trim().ToLowerInvariant(),
            DefaultLanguage = NormalizeOptional(request.DefaultLanguage),
            DefaultInstruction = NormalizeOptional(request.DefaultInstruction),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _templateRepository.AddAsync(entity, cancellationToken);
        await _templateRepository.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(MapToResponse(entity)));
    }

    [HttpPut("admin/formulas/{formulaId:guid}")]
    [Authorize("ADMIN", "Admin", "admin")]
    [ProducesResponseType(typeof(Result<PromptFormulaTemplateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid formulaId,
        [FromBody] UpsertPromptFormulaTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = ValidateAdminRequest(request);
        if (validationResult.IsFailure)
        {
            return HandleFailure(Result.Failure<PromptFormulaTemplateResponse>(validationResult.Error));
        }

        var entity = await _templateRepository.GetByIdAsync(formulaId, cancellationToken);
        if (entity is null)
        {
            return HandleFailure(Result.Failure<PromptFormulaTemplateResponse>(
                new Error("Formula.NotFound", "Formula template was not found.")));
        }

        var existingByKey = await _templateRepository.GetByKeyAsync(request.Key.Trim(), cancellationToken);
        if (existingByKey is not null && existingByKey.Id != entity.Id)
        {
            return HandleFailure(Result.Failure<PromptFormulaTemplateResponse>(
                new Error("Formula.AlreadyExists", "Formula key already exists.")));
        }

        entity.Key = request.Key.Trim();
        entity.Name = request.Name.Trim();
        entity.Template = request.Template.Trim();
        entity.OutputType = request.OutputType.Trim().ToLowerInvariant();
        entity.DefaultLanguage = NormalizeOptional(request.DefaultLanguage);
        entity.DefaultInstruction = NormalizeOptional(request.DefaultInstruction);
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _templateRepository.Update(entity);
        await _templateRepository.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(MapToResponse(entity)));
    }

    [HttpDelete("admin/formulas/{formulaId:guid}")]
    [Authorize("ADMIN", "Admin", "admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid formulaId, CancellationToken cancellationToken)
    {
        var entity = await _templateRepository.GetByIdAsync(formulaId, cancellationToken);
        if (entity is null)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Formula.NotFound", "Formula template was not found.")));
        }

        entity.IsActive = false;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _templateRepository.Update(entity);
        await _templateRepository.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<Result<ResolvedTemplateSource>> ResolveTemplateSourceAsync(
        FormulaGenerateRequest request,
        CancellationToken cancellationToken)
    {
        var formulaId = NormalizeGuid(request.FormulaId);
        if (formulaId.HasValue)
        {
            var entity = await _templateRepository.GetByIdAsync(formulaId.Value, cancellationToken);
            if (entity is null)
            {
                return Result.Failure<ResolvedTemplateSource>(
                    new Error("Formula.NotFound", "Formula template was not found."));
            }

            if (!entity.IsActive)
            {
                return Result.Failure<ResolvedTemplateSource>(
                    new Error("Formula.Inactive", "Formula template is inactive."));
            }

            return Result.Success(new ResolvedTemplateSource(entity, entity.Key, entity.Template, entity.DefaultLanguage, entity.DefaultInstruction));
        }

        if (!string.IsNullOrWhiteSpace(request.FormulaKey))
        {
            var key = request.FormulaKey.Trim();
            var entity = await _templateRepository.GetByKeyAsync(key, cancellationToken);
            if (entity is null)
            {
                return Result.Failure<ResolvedTemplateSource>(
                    new Error("Formula.NotFound", "Formula template was not found."));
            }

            if (!entity.IsActive)
            {
                return Result.Failure<ResolvedTemplateSource>(
                    new Error("Formula.Inactive", "Formula template is inactive."));
            }

            return Result.Success(new ResolvedTemplateSource(entity, entity.Key, entity.Template, entity.DefaultLanguage, entity.DefaultInstruction));
        }

        if (string.IsNullOrWhiteSpace(request.Template))
        {
            return Result.Failure<ResolvedTemplateSource>(
                new Error("Formula.TemplateMissing", "formulaId, formulaKey, or template is required."));
        }

        return Result.Success(new ResolvedTemplateSource(
            null,
            null,
            request.Template.Trim(),
            null,
            null));
    }

    private async Task RefundAsync(
        Guid userId,
        decimal totalCoins,
        string referenceId,
        AiSpendRecord spendRecord,
        CancellationToken cancellationToken)
    {
        var refundResult = await _billingClient.RefundAsync(
            userId,
            totalCoins,
            CoinDebitReasons.FormulaGenerationRefund,
            CoinReferenceTypes.FormulaGeneration,
            referenceId,
            cancellationToken);

        if (refundResult.IsFailure)
        {
            return;
        }

        spendRecord.Status = AiSpendStatuses.Refunded;
        spendRecord.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _aiSpendRecordRepository.Update(spendRecord);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);
    }

    private static Result<Dictionary<string, string>> NormalizeVariables(IDictionary<string, JsonElement>? variables)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (variables is null || variables.Count == 0)
        {
            return Result.Success(normalized);
        }

        foreach (var pair in variables)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var conversionResult = TryConvertScalarValue(pair.Key.Trim(), pair.Value);
            if (conversionResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(conversionResult.Error);
            }

            normalized[pair.Key.Trim()] = conversionResult.Value;
        }

        return Result.Success(normalized);
    }

    private static Result<string> TryConvertScalarValue(string key, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => Result.Success(value.GetString() ?? string.Empty),
            JsonValueKind.Number => Result.Success(value.ToString()),
            JsonValueKind.True => Result.Success(bool.TrueString.ToLowerInvariant()),
            JsonValueKind.False => Result.Success(bool.FalseString.ToLowerInvariant()),
            JsonValueKind.Null or JsonValueKind.Undefined => Result.Success(string.Empty),
            _ => Result.Failure<string>(
                new Error(
                    "Formula.InvalidVariableValue",
                    $"Variable '{key}' must be a scalar string, number, or boolean.",
                    new Dictionary<string, object?> { ["variable"] = key }))
        };
    }

    private static Result<string> NormalizeOutputType(string? outputType)
    {
        if (string.IsNullOrWhiteSpace(outputType))
        {
            return Result.Failure<string>(
                new Error("Formula.InvalidOutputType", "outputType is required."));
        }

        var normalized = outputType.Trim().ToLowerInvariant();
        if (!FormulaOutputTypes.Allowed.Contains(normalized))
        {
            return Result.Failure<string>(
                new Error(
                    "Formula.InvalidOutputType",
                    "outputType must be one of: caption, hook, cta, outline, custom.",
                    new Dictionary<string, object?> { ["outputType"] = normalized }));
        }

        return Result.Success(normalized);
    }

    private static Result<bool> ValidateAdminRequest(UpsertPromptFormulaTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Template))
        {
            return Result.Failure<bool>(
                new Error("Formula.InvalidRequest", "key, name, template, and outputType are required."));
        }

        var outputTypeResult = NormalizeOutputType(request.OutputType);
        if (outputTypeResult.IsFailure)
        {
            return Result.Failure<bool>(outputTypeResult.Error);
        }

        return Result.Success(true);
    }

    private static PromptFormulaTemplateResponse MapToResponse(PromptFormulaTemplate entity)
    {
        return new PromptFormulaTemplateResponse(
            entity.Id,
            entity.Key,
            entity.Name,
            entity.Template,
            entity.OutputType,
            entity.DefaultLanguage,
            entity.DefaultInstruction,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private static Guid? NormalizeGuid(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty ? value.Value : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ResolvedTemplateSource(
        PromptFormulaTemplate? TemplateEntity,
        string? FormulaKey,
        string Template,
        string? DefaultLanguage,
        string? DefaultInstruction);
}

public sealed class FormulaGenerateRequest
{
    public Guid? FormulaId { get; set; }
    public string? FormulaKey { get; set; }
    public string? Template { get; set; }
    public Dictionary<string, JsonElement>? Variables { get; set; }
    public string? OutputType { get; set; }
    public string? Language { get; set; }
    public string? Instruction { get; set; }
    public int? VariantCount { get; set; }
    public Guid? WorkspaceId { get; set; }
}

public sealed record UpsertPromptFormulaTemplateRequest(
    string Key,
    string Name,
    string Template,
    string OutputType,
    string? DefaultLanguage,
    string? DefaultInstruction,
    bool IsActive = true);

public sealed record PromptFormulaTemplateResponse(
    Guid Id,
    string Key,
    string Name,
    string Template,
    string OutputType,
    string? DefaultLanguage,
    string? DefaultInstruction,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
