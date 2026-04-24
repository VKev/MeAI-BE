using Application.Abstractions.ApiCredentials;
using Domain.Entities;
using Infrastructure.Context;
using Infrastructure.Logic.ApiCredentials;
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
[Route("api/Ai/admin/api-keys")]
[Authorize("ADMIN", "Admin", "admin")]
public sealed class AdminApiKeysController : ApiController
{
    private readonly MyDbContext _dbContext;
    private readonly ApiCredentialCryptoService _cryptoService;
    private readonly IApiCredentialProvider _credentialProvider;

    public AdminApiKeysController(
        IMediator mediator,
        MyDbContext dbContext,
        ApiCredentialCryptoService cryptoService,
        IApiCredentialProvider credentialProvider) : base(mediator)
    {
        _dbContext = dbContext;
        _cryptoService = cryptoService;
        _credentialProvider = credentialProvider;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ApiCredentialResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? provider,
        [FromQuery] bool? isActive,
        [FromQuery] string? keyName,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.ApiCredentials
            .AsNoTracking()
            .Where(item => !item.IsDeleted && item.ServiceName == ApiCredentialCatalog.ServiceName);

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = provider.Trim();
            query = query.Where(item => item.Provider == normalizedProvider);
        }

        if (!string.IsNullOrWhiteSpace(keyName))
        {
            var normalizedKeyName = keyName.Trim();
            query = query.Where(item => item.KeyName == normalizedKeyName);
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        var items = await query
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.KeyName)
            .ToListAsync(cancellationToken);

        return Ok(Result.Success<IReadOnlyList<ApiCredentialResponse>>(items.Select(MapToResponse).ToList()));
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<ApiCredentialResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateApiCredentialRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.KeyName) ||
            string.IsNullOrWhiteSpace(request.Value))
        {
            return HandleFailure(Result.Failure<ApiCredentialResponse>(
                new Error("ApiCredential.InvalidRequest", "Provider, keyName, and value are required.")));
        }

        var provider = request.Provider.Trim();
        var keyNameValue = request.KeyName.Trim();
        var existing = await _dbContext.ApiCredentials.FirstOrDefaultAsync(item =>
                !item.IsDeleted &&
                item.ServiceName == ApiCredentialCatalog.ServiceName &&
                item.Provider == provider &&
                item.KeyName == keyNameValue,
            cancellationToken);

        if (existing is not null)
        {
            return HandleFailure(Result.Failure<ApiCredentialResponse>(
                new Error("ApiCredential.AlreadyExists", "API credential already exists.")));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var value = request.Value.Trim();
        var credential = new ApiCredential
        {
            Id = Guid.CreateVersion7(),
            ServiceName = ApiCredentialCatalog.ServiceName,
            Provider = provider,
            KeyName = keyNameValue,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? $"{provider} {keyNameValue}"
                : request.DisplayName.Trim(),
            ValueEncrypted = _cryptoService.Encrypt(value),
            ValueLast4 = GetLast4(value),
            IsActive = request.IsActive,
            Source = "admin_created",
            Version = 1,
            LastRotatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.ApiCredentials.Add(credential);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _credentialProvider.StoreValue(provider, keyNameValue, credential.IsActive ? value : null);

        return Ok(Result.Success(MapToResponse(credential)));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Result<ApiCredentialResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateApiCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var credential = await _dbContext.ApiCredentials.FirstOrDefaultAsync(item =>
                item.Id == id &&
                !item.IsDeleted &&
                item.ServiceName == ApiCredentialCatalog.ServiceName,
            cancellationToken);

        if (credential is null)
        {
            return HandleFailure(Result.Failure<ApiCredentialResponse>(
                new Error("ApiCredential.NotFound", "API credential not found.")));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        string? resolvedValue = null;

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            credential.DisplayName = request.DisplayName.Trim();
        }

        if (request.IsActive.HasValue)
        {
            credential.IsActive = request.IsActive.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Value))
        {
            resolvedValue = request.Value.Trim();
            credential.ValueEncrypted = _cryptoService.Encrypt(resolvedValue);
            credential.ValueLast4 = GetLast4(resolvedValue);
            credential.LastRotatedAt = now;
        }

        credential.Source = "admin_updated";
        credential.Version = credential.Version <= 0 ? 1 : credential.Version + 1;
        credential.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!credential.IsActive)
        {
            _credentialProvider.StoreValue(credential.Provider, credential.KeyName, null);
        }
        else if (resolvedValue is not null)
        {
            _credentialProvider.StoreValue(credential.Provider, credential.KeyName, resolvedValue);
        }
        else
        {
            _credentialProvider.Invalidate(credential.Provider, credential.KeyName);
        }

        return Ok(Result.Success(MapToResponse(credential)));
    }

    private static ApiCredentialResponse MapToResponse(ApiCredential credential)
    {
        return new ApiCredentialResponse(
            credential.Id,
            credential.ServiceName,
            credential.Provider,
            credential.KeyName,
            credential.DisplayName,
            string.IsNullOrWhiteSpace(credential.ValueLast4) ? "****" : $"****{credential.ValueLast4}",
            credential.IsActive,
            credential.Source,
            credential.Version,
            credential.LastSyncedFromEnvAt,
            credential.LastRotatedAt,
            credential.CreatedAt,
            credential.UpdatedAt);
    }

    private static string? GetLast4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 4 ? value : value[^4..];
    }
}

public sealed record CreateApiCredentialRequest(
    string Provider,
    string KeyName,
    string? DisplayName,
    string Value,
    bool IsActive = true);

public sealed record UpdateApiCredentialRequest(
    string? DisplayName,
    string? Value,
    bool? IsActive);

public sealed record ApiCredentialResponse(
    Guid Id,
    string ServiceName,
    string Provider,
    string KeyName,
    string DisplayName,
    string MaskedValue,
    bool IsActive,
    string Source,
    int Version,
    DateTime? LastSyncedFromEnvAt,
    DateTime? LastRotatedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
