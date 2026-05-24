using System.Text.Json;
using Domain.Entities;
using Infrastructure.Context;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/generation-options")]
[Authorize]
public sealed class GenerationOptionsController : ApiController
{
    private readonly MyDbContext _dbContext;

    public GenerationOptionsController(IMediator mediator, MyDbContext dbContext)
        : base(mediator)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<GenerationOptionsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var response = await GenerationOptionsMapper.BuildResponseAsync(
            _dbContext,
            includeInactive: false,
            cancellationToken);

        return Ok(Result.Success(response));
    }
}

[ApiController]
[Route("api/Ai/admin/generation-options")]
[Authorize("ADMIN", "Admin", "admin")]
public sealed class AdminGenerationOptionsController : ApiController
{
    private readonly MyDbContext _dbContext;

    public AdminGenerationOptionsController(IMediator mediator, MyDbContext dbContext)
        : base(mediator)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<GenerationOptionsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await GenerationOptionsMapper.BuildResponseAsync(
            _dbContext,
            includeInactive: true,
            cancellationToken);

        return Ok(Result.Success(response));
    }

    [HttpPost("models")]
    [ProducesResponseType(typeof(Result<GenerationModelOptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateModel(
        [FromBody] UpsertGenerationModelOptionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = NormalizeModelRequest(request);
        if (validation.IsFailure)
        {
            return HandleFailure(Result.Failure<GenerationModelOptionResponse>(validation.Error));
        }

        var input = validation.Value;
        var duplicate = await _dbContext.GenerationModelOptions.AnyAsync(item =>
                item.DeletedAt == null &&
                item.Mode == input.Mode &&
                item.ModelId == input.ModelId,
            cancellationToken);
        if (duplicate)
        {
            return HandleFailure(Result.Failure<GenerationModelOptionResponse>(
                new Error("GenerationOptions.ModelAlreadyExists", "A model option with this mode and model id already exists.")));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var entity = new GenerationModelOption
        {
            Id = Guid.CreateVersion7(),
            Mode = input.Mode,
            ModelId = input.ModelId,
            Name = input.Name,
            Description = input.Description,
            SupportedRatiosJson = JsonSerializer.Serialize(input.SupportedRatios),
            SupportedQualitiesJson = JsonSerializer.Serialize(input.SupportedQualities),
            SupportsResolution = input.SupportsResolution,
            IsActive = input.IsActive,
            SortOrder = input.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.GenerationModelOptions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(GenerationOptionsMapper.MapModel(entity)));
    }

    [HttpPut("models/{id:guid}")]
    [ProducesResponseType(typeof(Result<GenerationModelOptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateModel(
        Guid id,
        [FromBody] UpsertGenerationModelOptionRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.GenerationModelOptions
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
        if (entity is null)
        {
            return HandleFailure(Result.Failure<GenerationModelOptionResponse>(
                new Error("GenerationOptions.ModelNotFound", "Generation model option not found.")));
        }

        var validation = NormalizeModelRequest(request);
        if (validation.IsFailure)
        {
            return HandleFailure(Result.Failure<GenerationModelOptionResponse>(validation.Error));
        }

        var input = validation.Value;
        var duplicate = await _dbContext.GenerationModelOptions.AnyAsync(item =>
                item.Id != id &&
                item.DeletedAt == null &&
                item.Mode == input.Mode &&
                item.ModelId == input.ModelId,
            cancellationToken);
        if (duplicate)
        {
            return HandleFailure(Result.Failure<GenerationModelOptionResponse>(
                new Error("GenerationOptions.ModelAlreadyExists", "A model option with this mode and model id already exists.")));
        }

        entity.Mode = input.Mode;
        entity.ModelId = input.ModelId;
        entity.Name = input.Name;
        entity.Description = input.Description;
        entity.SupportedRatiosJson = JsonSerializer.Serialize(input.SupportedRatios);
        entity.SupportedQualitiesJson = JsonSerializer.Serialize(input.SupportedQualities);
        entity.SupportsResolution = input.SupportsResolution;
        entity.IsActive = input.IsActive;
        entity.SortOrder = input.SortOrder;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(GenerationOptionsMapper.MapModel(entity)));
    }

    [HttpDelete("models/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteModel(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.GenerationModelOptions
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
        if (entity is null)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("GenerationOptions.ModelNotFound", "Generation model option not found.")));
        }

        entity.IsActive = false;
        entity.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        entity.UpdatedAt = entity.DeletedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("social-presets")]
    [ProducesResponseType(typeof(Result<GenerationSocialPresetResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSocialPreset(
        [FromBody] UpsertGenerationSocialPresetRequest request,
        CancellationToken cancellationToken)
    {
        var validation = NormalizeSocialRequest(request);
        if (validation.IsFailure)
        {
            return HandleFailure(Result.Failure<GenerationSocialPresetResponse>(validation.Error));
        }

        var input = validation.Value;
        var duplicate = await _dbContext.GenerationSocialPresets.AnyAsync(item =>
                item.DeletedAt == null &&
                item.Mode == input.Mode &&
                item.Platform == input.Platform &&
                item.ContentType == input.ContentType,
            cancellationToken);
        if (duplicate)
        {
            return HandleFailure(Result.Failure<GenerationSocialPresetResponse>(
                new Error("GenerationOptions.SocialPresetAlreadyExists", "A social preset with this mode, platform, and content type already exists.")));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var entity = new GenerationSocialPreset
        {
            Id = Guid.CreateVersion7(),
            Mode = input.Mode,
            Platform = input.Platform,
            Label = input.Label,
            ContentType = input.ContentType,
            ContentLabel = input.ContentLabel,
            SupportedRatiosJson = JsonSerializer.Serialize(input.SupportedRatios),
            DefaultRatio = input.DefaultRatio,
            IsActive = input.IsActive,
            SortOrder = input.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.GenerationSocialPresets.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(GenerationOptionsMapper.MapSocialPreset(entity)));
    }

    [HttpPut("social-presets/{id:guid}")]
    [ProducesResponseType(typeof(Result<GenerationSocialPresetResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSocialPreset(
        Guid id,
        [FromBody] UpsertGenerationSocialPresetRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.GenerationSocialPresets
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
        if (entity is null)
        {
            return HandleFailure(Result.Failure<GenerationSocialPresetResponse>(
                new Error("GenerationOptions.SocialPresetNotFound", "Generation social preset not found.")));
        }

        var validation = NormalizeSocialRequest(request);
        if (validation.IsFailure)
        {
            return HandleFailure(Result.Failure<GenerationSocialPresetResponse>(validation.Error));
        }

        var input = validation.Value;
        var duplicate = await _dbContext.GenerationSocialPresets.AnyAsync(item =>
                item.Id != id &&
                item.DeletedAt == null &&
                item.Mode == input.Mode &&
                item.Platform == input.Platform &&
                item.ContentType == input.ContentType,
            cancellationToken);
        if (duplicate)
        {
            return HandleFailure(Result.Failure<GenerationSocialPresetResponse>(
                new Error("GenerationOptions.SocialPresetAlreadyExists", "A social preset with this mode, platform, and content type already exists.")));
        }

        entity.Mode = input.Mode;
        entity.Platform = input.Platform;
        entity.Label = input.Label;
        entity.ContentType = input.ContentType;
        entity.ContentLabel = input.ContentLabel;
        entity.SupportedRatiosJson = JsonSerializer.Serialize(input.SupportedRatios);
        entity.DefaultRatio = input.DefaultRatio;
        entity.IsActive = input.IsActive;
        entity.SortOrder = input.SortOrder;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(GenerationOptionsMapper.MapSocialPreset(entity)));
    }

    [HttpDelete("social-presets/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteSocialPreset(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.GenerationSocialPresets
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
        if (entity is null)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("GenerationOptions.SocialPresetNotFound", "Generation social preset not found.")));
        }

        entity.IsActive = false;
        entity.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        entity.UpdatedAt = entity.DeletedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static Result<NormalizedModelInput> NormalizeModelRequest(UpsertGenerationModelOptionRequest request)
    {
        var mode = NormalizeMode(request.Mode);
        if (mode is null)
        {
            return Result.Failure<NormalizedModelInput>(
                new Error("GenerationOptions.InvalidMode", "Mode must be 'image' or 'video'."));
        }

        var modelId = NormalizeRequired(request.ModelId);
        var name = NormalizeRequired(request.Name);
        if (modelId is null || name is null)
        {
            return Result.Failure<NormalizedModelInput>(
                new Error("GenerationOptions.InvalidModel", "Model id and name are required."));
        }

        var ratios = NormalizeList(request.SupportedRatios);
        if (ratios.Count == 0)
        {
            return Result.Failure<NormalizedModelInput>(
                new Error("GenerationOptions.InvalidRatios", "At least one supported ratio is required."));
        }

        var qualities = NormalizeList(request.SupportedQualities);
        if (request.SupportsResolution && qualities.Count == 0)
        {
            return Result.Failure<NormalizedModelInput>(
                new Error("GenerationOptions.InvalidQualities", "Resolution-capable models require at least one supported quality."));
        }

        return Result.Success(new NormalizedModelInput(
            mode,
            modelId,
            name,
            NormalizeOptional(request.Description),
            ratios,
            qualities,
            request.SupportsResolution,
            request.IsActive,
            request.SortOrder));
    }

    private static Result<NormalizedSocialInput> NormalizeSocialRequest(UpsertGenerationSocialPresetRequest request)
    {
        var mode = NormalizeMode(request.Mode);
        if (mode is null)
        {
            return Result.Failure<NormalizedSocialInput>(
                new Error("GenerationOptions.InvalidMode", "Mode must be 'image' or 'video'."));
        }

        var platform = NormalizeRequired(request.Platform)?.ToLowerInvariant();
        var label = NormalizeRequired(request.Label);
        var contentType = NormalizeRequired(request.ContentType)?.ToLowerInvariant();
        var contentLabel = NormalizeRequired(request.ContentLabel);
        var defaultRatio = NormalizeRequired(request.DefaultRatio);

        if (platform is null || label is null || contentType is null || contentLabel is null || defaultRatio is null)
        {
            return Result.Failure<NormalizedSocialInput>(
                new Error("GenerationOptions.InvalidSocialPreset", "Platform, label, content type, content label, and default ratio are required."));
        }

        var ratios = NormalizeList(request.SupportedRatios);
        if (ratios.Count == 0)
        {
            return Result.Failure<NormalizedSocialInput>(
                new Error("GenerationOptions.InvalidRatios", "At least one supported ratio is required."));
        }

        if (!ratios.Contains(defaultRatio, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<NormalizedSocialInput>(
                new Error("GenerationOptions.InvalidDefaultRatio", "Default ratio must be included in supported ratios."));
        }

        return Result.Success(new NormalizedSocialInput(
            mode,
            platform,
            label,
            contentType,
            contentLabel,
            ratios,
            defaultRatio,
            request.IsActive,
            request.SortOrder));
    }

    private static string? NormalizeMode(string? value)
    {
        var normalized = NormalizeRequired(value)?.ToLowerInvariant();
        return normalized is "image" or "video" ? normalized : null;
    }

    private static string? NormalizeRequired(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Select(item => item?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record NormalizedModelInput(
        string Mode,
        string ModelId,
        string Name,
        string? Description,
        IReadOnlyList<string> SupportedRatios,
        IReadOnlyList<string> SupportedQualities,
        bool SupportsResolution,
        bool IsActive,
        int SortOrder);

    private sealed record NormalizedSocialInput(
        string Mode,
        string Platform,
        string Label,
        string ContentType,
        string ContentLabel,
        IReadOnlyList<string> SupportedRatios,
        string DefaultRatio,
        bool IsActive,
        int SortOrder);
}

internal static class GenerationOptionsMapper
{
    internal static async Task<GenerationOptionsResponse> BuildResponseAsync(
        MyDbContext dbContext,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var modelsQuery = dbContext.GenerationModelOptions
            .AsNoTracking()
            .Where(item => item.DeletedAt == null);

        var presetsQuery = dbContext.GenerationSocialPresets
            .AsNoTracking()
            .Where(item => item.DeletedAt == null);

        if (!includeInactive)
        {
            modelsQuery = modelsQuery.Where(item => item.IsActive);
            presetsQuery = presetsQuery.Where(item => item.IsActive);
        }

        var models = await modelsQuery
            .OrderBy(item => item.Mode)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var presets = await presetsQuery
            .OrderBy(item => item.Mode)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Label)
            .ThenBy(item => item.ContentLabel)
            .ToListAsync(cancellationToken);

        return new GenerationOptionsResponse(
            models.Select(MapModel).ToList(),
            presets.Select(MapSocialPreset).ToList());
    }

    internal static GenerationModelOptionResponse MapModel(GenerationModelOption item) =>
        new(
            item.Id,
            item.Mode,
            item.ModelId,
            item.Name,
            item.Description,
            ParseStringList(item.SupportedRatiosJson),
            ParseStringList(item.SupportedQualitiesJson),
            item.SupportsResolution,
            item.IsActive,
            item.SortOrder,
            item.CreatedAt,
            item.UpdatedAt);

    internal static GenerationSocialPresetResponse MapSocialPreset(GenerationSocialPreset item) =>
        new(
            item.Id,
            item.Mode,
            item.Platform,
            item.Label,
            item.ContentType,
            item.ContentLabel,
            ParseStringList(item.SupportedRatiosJson),
            item.DefaultRatio,
            item.IsActive,
            item.SortOrder,
            item.CreatedAt,
            item.UpdatedAt);

    private static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed record GenerationOptionsResponse(
    IReadOnlyList<GenerationModelOptionResponse> Models,
    IReadOnlyList<GenerationSocialPresetResponse> SocialPresets);

public sealed record GenerationModelOptionResponse(
    Guid Id,
    string Mode,
    string ModelId,
    string Name,
    string? Description,
    IReadOnlyList<string> SupportedRatios,
    IReadOnlyList<string> SupportedQualities,
    bool SupportsResolution,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record GenerationSocialPresetResponse(
    Guid Id,
    string Mode,
    string Platform,
    string Label,
    string ContentType,
    string ContentLabel,
    IReadOnlyList<string> SupportedRatios,
    string DefaultRatio,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record UpsertGenerationModelOptionRequest(
    string? Mode,
    string? ModelId,
    string? Name,
    string? Description,
    IReadOnlyList<string>? SupportedRatios,
    IReadOnlyList<string>? SupportedQualities,
    bool SupportsResolution,
    bool IsActive,
    int SortOrder);

public sealed record UpsertGenerationSocialPresetRequest(
    string? Mode,
    string? Platform,
    string? Label,
    string? ContentType,
    string? ContentLabel,
    IReadOnlyList<string>? SupportedRatios,
    string? DefaultRatio,
    bool IsActive,
    int SortOrder);
