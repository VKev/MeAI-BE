using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.ApiCredentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Kie;

public sealed class KieResponsesClient
{
    public const string DefaultChatModel = "gpt-5-4";

    private const string DefaultBaseUrl = "https://api.kie.ai";
    private const string ResponsesPath = "/codex/v1/responses";

    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly IApiCredentialProvider _credentialProvider;
    private readonly ILogger<KieResponsesClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public KieResponsesClient(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IApiCredentialProvider credentialProvider,
        ILogger<KieResponsesClient> logger)
    {
        _baseUrl = (configuration["Kie:BaseUrl"] ?? configuration["Kie__BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');
        _httpClient = httpClientFactory.CreateClient("KieChat");
        _credentialProvider = credentialProvider;
        _logger = logger;
    }

    public async Task<Result<string>> GetTextResponseAsync(
        string model,
        IReadOnlyList<KieResponsesInputItem> input,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken,
        IReadOnlyList<KieResponsesTool>? tools = null,
        string? toolChoice = null,
        string? reasoningEffort = null)
    {
        var payload = BuildRequest(model, input, tools, toolChoice, reasoningEffort);
        var bodyResult = await SendAsync(payload, failureCode, failureMessage, cancellationToken);
        if (bodyResult.IsFailure)
        {
            return Result.Failure<string>(bodyResult.Error);
        }

        try
        {
            var text = ExtractOutputText(bodyResult.Value);
            return string.IsNullOrWhiteSpace(text)
                ? Result.Failure<string>(new Error(failureCode, "Kie returned an empty response."))
                : Result.Success(text.Trim());
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(new Error(failureCode, $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<string>> GetFunctionArgumentsAsync(
        string model,
        IReadOnlyList<KieResponsesInputItem> input,
        KieResponsesFunctionTool tool,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken,
        string? reasoningEffort = null)
    {
        var payload = BuildRequest(
            model,
            input,
            [tool],
            new KieResponsesFunctionToolChoice { Name = tool.Name },
            reasoningEffort);
        var bodyResult = await SendAsync(payload, failureCode, failureMessage, cancellationToken);
        if (bodyResult.IsFailure)
        {
            return Result.Failure<string>(bodyResult.Error);
        }

        try
        {
            var arguments = ExtractFunctionArguments(bodyResult.Value, tool.Name);
            return string.IsNullOrWhiteSpace(arguments)
                ? Result.Failure<string>(new Error(failureCode, $"Kie did not call the required tool: {tool.Name}."))
                : Result.Success(arguments);
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(new Error(failureCode, $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<string>> CreateRawResponseAsync(
        string model,
        IReadOnlyList<KieResponsesInputItem> input,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken,
        IReadOnlyList<KieResponsesTool>? tools = null,
        object? toolChoice = null,
        string? reasoningEffort = null)
    {
        var payload = BuildRequest(model, input, tools, toolChoice, reasoningEffort);
        return await SendAsync(payload, failureCode, failureMessage, cancellationToken);
    }

    private static KieResponsesRequest BuildRequest(
        string model,
        IReadOnlyList<KieResponsesInputItem> input,
        IReadOnlyList<KieResponsesTool>? tools,
        object? toolChoice,
        string? reasoningEffort)
    {
        return new KieResponsesRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? DefaultChatModel : model.Trim(),
            Stream = false,
            Input = input.ToList(),
            Tools = tools?.ToList(),
            ToolChoice = toolChoice,
            Reasoning = string.IsNullOrWhiteSpace(reasoningEffort)
                ? null
                : new KieResponsesReasoning { Effort = reasoningEffort.Trim() }
        };
    }

    private async Task<Result<string>> SendAsync(
        KieResponsesRequest payload,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{ResponsesPath}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _credentialProvider.GetRequiredValue("Kie", "ApiKey"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Kie Responses request failed.");
            return Result.Failure<string>(new Error(failureCode, $"Network error: {ex.Message}"));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryReadKieErrorMessage(body) ?? failureMessage;
            return Result.Failure<string>(new Error(failureCode, errorMessage));
        }

        return Result.Success(body);
    }

    public static KieResponsesInputItem UserText(string text)
    {
        return new KieResponsesInputItem
        {
            Role = "user",
            Content = [new KieResponsesContentPart { Type = "input_text", Text = text }]
        };
    }

    public static KieResponsesInputItem UserParts(IReadOnlyList<KieResponsesContentPart> parts)
    {
        return new KieResponsesInputItem
        {
            Role = "user",
            Content = parts.ToList()
        };
    }

    public static KieResponsesInputItem FunctionCall(string callId, string name, string arguments)
    {
        return new KieResponsesInputItem
        {
            Type = "function_call",
            CallId = callId,
            Name = name,
            Arguments = arguments
        };
    }

    public static KieResponsesInputItem FunctionCallOutput(string callId, string output)
    {
        return new KieResponsesInputItem
        {
            Type = "function_call_output",
            CallId = callId,
            Output = output
        };
    }

    private static string ExtractOutputText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProp.GetString();
                if (type is not ("output_text" or "text"))
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    buffer.Append(textProp.GetString());
                }
            }
        }

        return buffer.ToString();
    }

    private static string? ExtractFunctionArguments(string body, string toolName)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            var direct = TryReadFunctionArguments(item, toolName);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                var nested = TryReadFunctionArguments(part, toolName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<KieResponsesFunctionCall> ExtractFunctionCalls(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calls = new List<KieResponsesFunctionCall>();
        foreach (var item in output.EnumerateArray())
        {
            TryAppendFunctionCall(calls, item);

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                TryAppendFunctionCall(calls, part);
            }
        }

        return calls;
    }

    public static string? ExtractText(string body)
    {
        return ExtractOutputText(body);
    }

    private static void TryAppendFunctionCall(List<KieResponsesFunctionCall> calls, JsonElement element)
    {
        if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var type = typeProp.GetString();
        if (type is not ("function_call" or "tool_call"))
        {
            return;
        }

        var name = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString()
            : null;
        var callId = element.TryGetProperty("call_id", out var callIdProp) && callIdProp.ValueKind == JsonValueKind.String
            ? callIdProp.GetString()
            : null;

        string? arguments = null;
        if (element.TryGetProperty("arguments", out var argsProp))
        {
            arguments = argsProp.ValueKind == JsonValueKind.String ? argsProp.GetString() : argsProp.GetRawText();
        }
        else if (element.TryGetProperty("function", out var functionProp))
        {
            if (string.IsNullOrWhiteSpace(name) &&
                functionProp.TryGetProperty("name", out var nestedNameProp) &&
                nestedNameProp.ValueKind == JsonValueKind.String)
            {
                name = nestedNameProp.GetString();
            }

            if (functionProp.TryGetProperty("arguments", out var nestedArgsProp))
            {
                arguments = nestedArgsProp.ValueKind == JsonValueKind.String
                    ? nestedArgsProp.GetString()
                    : nestedArgsProp.GetRawText();
            }
        }

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(callId))
        {
            calls.Add(new KieResponsesFunctionCall(
                callId!,
                name!,
                arguments ?? "{}"));
        }
    }

    private static string? TryReadFunctionArguments(JsonElement element, string toolName)
    {
        if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var type = typeProp.GetString();
        if (type is not ("function_call" or "tool_call"))
        {
            return null;
        }

        if (element.TryGetProperty("name", out var nameProp) &&
            nameProp.ValueKind == JsonValueKind.String &&
            !string.Equals(nameProp.GetString(), toolName, StringComparison.Ordinal))
        {
            return null;
        }

        if (element.TryGetProperty("arguments", out var argsProp))
        {
            return argsProp.ValueKind == JsonValueKind.String
                ? argsProp.GetString()
                : argsProp.GetRawText();
        }

        if (element.TryGetProperty("function", out var functionProp) &&
            functionProp.TryGetProperty("arguments", out var nestedArgsProp))
        {
            return nestedArgsProp.ValueKind == JsonValueKind.String
                ? nestedArgsProp.GetString()
                : nestedArgsProp.GetRawText();
        }

        return null;
    }

    private static string? TryReadKieErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }

            if (document.RootElement.TryGetProperty("message", out var rootMessage) &&
                rootMessage.ValueKind == JsonValueKind.String)
            {
                return rootMessage.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed class KieResponsesRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = DefaultChatModel;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("input")]
        public List<KieResponsesInputItem> Input { get; set; } = new();

        [JsonPropertyName("tools")]
        public List<KieResponsesTool>? Tools { get; set; }

        [JsonPropertyName("tool_choice")]
        public object? ToolChoice { get; set; }

        [JsonPropertyName("reasoning")]
        public KieResponsesReasoning? Reasoning { get; set; }
    }
}

public sealed class KieResponsesInputItem
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public List<KieResponsesContentPart>? Content { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }
}

public sealed class KieResponsesContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "input_text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }
}

public class KieResponsesTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public sealed class KieResponsesFunctionTool : KieResponsesTool
{
    public KieResponsesFunctionTool()
    {
        Type = "function";
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object Parameters { get; set; } = new();

    [JsonPropertyName("strict")]
    public bool Strict { get; set; } = true;
}

public sealed class KieResponsesFunctionToolChoice
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class KieResponsesReasoning
{
    [JsonPropertyName("effort")]
    public string Effort { get; set; } = "low";
}

public sealed record KieResponsesFunctionCall(
    string CallId,
    string Name,
    string Arguments);
