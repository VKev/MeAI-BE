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
        _logger.LogInformation(
            "Kie Responses text request. Model={Model} Tools={Tools} ToolChoice={ToolChoice} ReasoningEffort={ReasoningEffort} InputPreview={InputPreview}",
            model,
            SummarizeTools(tools),
            SummarizeToolChoice(toolChoice),
            reasoningEffort ?? "<none>",
            SummarizeInput(input));

        var payload = BuildRequest(model, input, tools, toolChoice, reasoningEffort);
        var bodyResult = await SendAsync(payload, failureCode, failureMessage, cancellationToken);
        if (bodyResult.IsFailure)
        {
            return Result.Failure<string>(bodyResult.Error);
        }

        try
        {
            var text = ExtractText(bodyResult.Value) ?? string.Empty;
            _logger.LogInformation(
                "Kie Responses text parsed. Model={Model} TextLength={TextLength} TextPreview={TextPreview}",
                model,
                text.Length,
                Preview(text));

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
        _logger.LogInformation(
            "Kie Responses function request. Model={Model} Tool={Tool} ReasoningEffort={ReasoningEffort} InputPreview={InputPreview}",
            model,
            tool.Name,
            reasoningEffort ?? "<none>",
            SummarizeInput(input));

        var payload = BuildRequest(
            model,
            input,
            [tool],
            "auto",
            reasoningEffort);
        var bodyResult = await SendAsync(payload, failureCode, failureMessage, cancellationToken);
        if (bodyResult.IsFailure)
        {
            return Result.Failure<string>(bodyResult.Error);
        }

        try
        {
            var functionCalls = ExtractFunctionCalls(bodyResult.Value);
            var parseSource = "missing";
            var arguments = ExtractFunctionArguments(bodyResult.Value, tool.Name, out parseSource);
            if (string.IsNullOrWhiteSpace(arguments))
            {
                _logger.LogWarning(
                    "Kie Responses required tool missing from structured output. Tool={Tool} ParseSource={ParseSource} AvailableCalls={AvailableCalls} ResponsePreview={ResponsePreview}",
                    tool.Name,
                    parseSource,
                    SummarizeFunctionCalls(functionCalls),
                    Preview(bodyResult.Value, 4000));

                arguments = TryExtractJsonTextFallback(bodyResult.Value);
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    parseSource = "json_text_fallback";
                    _logger.LogWarning(
                        "Kie Responses function request fell back to JSON text. Tool={Tool} ParseSource={ParseSource} ArgumentsPreview={ArgumentsPreview}",
                        tool.Name,
                        parseSource,
                        Preview(arguments));
                }
            }
            else
            {
                _logger.LogInformation(
                    "Kie Responses function parsed structured call. Tool={Tool} ParseSource={ParseSource} AvailableCalls={AvailableCalls} ArgumentsPreview={ArgumentsPreview}",
                    tool.Name,
                    parseSource,
                    SummarizeFunctionCalls(functionCalls),
                    Preview(arguments));
            }

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

    public static string ResolveResponsesModel(string? preferredModel, string? configuredModel = null)
    {
        if (IsSupportedResponsesModel(preferredModel))
        {
            return preferredModel!.Trim();
        }

        if (IsSupportedResponsesModel(configuredModel))
        {
            return configuredModel!.Trim();
        }

        return DefaultChatModel;
    }

    public static bool IsSupportedResponsesModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var normalized = model.Trim().ToLowerInvariant();
        return normalized.StartsWith("gpt-5", StringComparison.Ordinal) ||
               normalized.Contains("codex", StringComparison.Ordinal);
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
            Tools = tools?.Cast<object>().ToList(),
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
        _logger.LogInformation(
            "Sending Kie Responses request. Url={Url} PayloadPreview={PayloadPreview}",
            $"{_baseUrl}{ResponsesPath}",
            Preview(json, 4000));

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
        _logger.LogInformation(
            "Received Kie Responses response. StatusCode={StatusCode} BodyPreview={BodyPreview}",
            (int)response.StatusCode,
            Preview(body, 4000));

        if (TryReadWrappedKieError(body, out var wrappedErrorMessage, out var wrappedErrorCode))
        {
            _logger.LogWarning(
                "Kie Responses returned an application-level error envelope. StatusCode={StatusCode} ErrorCode={ErrorCode} ErrorMessage={ErrorMessage}",
                (int)response.StatusCode,
                wrappedErrorCode?.ToString() ?? "<none>",
                wrappedErrorMessage);

            return Result.Failure<string>(new Error(
                failureCode,
                wrappedErrorCode is null
                    ? wrappedErrorMessage ?? failureMessage
                    : $"Kie returned error code {wrappedErrorCode}: {wrappedErrorMessage}"));
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryReadKieErrorMessage(body) ?? failureMessage;
            return Result.Failure<string>(new Error(failureCode, errorMessage));
        }

        return Result.Success(body);
    }

    public static KieResponsesInputItem UserText(string text)
    {
        return Message("user", text);
    }

    public static KieResponsesInputItem DeveloperText(string text)
    {
        return Message("developer", text);
    }

    public static KieResponsesInputItem SystemText(string text)
    {
        return Message("system", text);
    }

    private static KieResponsesInputItem Message(string role, string text)
    {
        return new KieResponsesInputItem
        {
            Role = role,
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

    private static string? ExtractFunctionArguments(string body, string toolName, out string parseSource)
    {
        parseSource = "missing";
        using var document = JsonDocument.Parse(body);
        if (TryReadFunctionArgumentsFromOutput(document.RootElement, toolName, out var outputArguments))
        {
            parseSource = "output";
            return outputArguments;
        }

        if (TryReadFunctionArgumentsFromChoices(document.RootElement, toolName, out var choiceArguments))
        {
            parseSource = "choices";
            return choiceArguments;
        }

        return null;
    }

    public static IReadOnlyList<KieResponsesFunctionCall> ExtractFunctionCalls(string body)
    {
        using var document = JsonDocument.Parse(body);
        var calls = new List<KieResponsesFunctionCall>();
        TryAppendFunctionCallsFromOutput(calls, document.RootElement);
        TryAppendFunctionCallsFromChoices(calls, document.RootElement);
        return calls;
    }

    public static string? ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        return TryExtractTextFromOutput(document.RootElement) ??
               TryExtractTextFromChoices(document.RootElement) ??
               string.Empty;
    }

    private static bool TryReadFunctionArgumentsFromOutput(
        JsonElement root,
        string toolName,
        out string? arguments)
    {
        arguments = null;
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in output.EnumerateArray())
        {
            var direct = TryReadFunctionArguments(item, toolName);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                arguments = direct;
                return true;
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
                    arguments = nested;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadFunctionArgumentsFromChoices(
        JsonElement root,
        string toolName,
        out string? arguments)
    {
        arguments = null;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var direct = TryReadFunctionArguments(message, toolName);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                arguments = direct;
                return true;
            }
        }

        return false;
    }

    private static void TryAppendFunctionCallsFromOutput(List<KieResponsesFunctionCall> calls, JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return;
        }

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
    }

    private static void TryAppendFunctionCallsFromChoices(List<KieResponsesFunctionCall> calls, JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            TryAppendFunctionCall(calls, message);
        }
    }

    private static string? TryExtractTextFromOutput(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return null;
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

    private static string? TryExtractTextFromChoices(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var buffer = new StringBuilder();
        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (message.TryGetProperty("content", out var contentProp))
            {
                switch (contentProp.ValueKind)
                {
                    case JsonValueKind.String:
                        buffer.Append(contentProp.GetString());
                        break;
                    case JsonValueKind.Array:
                        foreach (var part in contentProp.EnumerateArray())
                        {
                            if (part.ValueKind == JsonValueKind.String)
                            {
                                buffer.Append(part.GetString());
                                continue;
                            }

                            if (part.ValueKind == JsonValueKind.Object &&
                                part.TryGetProperty("text", out var textProp) &&
                                textProp.ValueKind == JsonValueKind.String)
                            {
                                buffer.Append(textProp.GetString());
                            }
                        }

                        break;
                }
            }
        }

        return buffer.ToString();
    }

    private static void TryAppendFunctionCall(List<KieResponsesFunctionCall> calls, JsonElement element)
    {
        if (TryBuildFunctionCall(element, out var directCall))
        {
            calls.Add(directCall);
        }

        if (!element.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (TryBuildFunctionCall(toolCall, out var nestedCall))
            {
                calls.Add(nestedCall);
            }
        }
    }

    private static string? TryReadFunctionArguments(JsonElement element, string toolName)
    {
        if (TryBuildFunctionCall(element, out var directCall) &&
            string.Equals(directCall.Name, toolName, StringComparison.Ordinal))
        {
            return directCall.Arguments;
        }

        if (!element.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (TryBuildFunctionCall(toolCall, out var nestedCall) &&
                string.Equals(nestedCall.Name, toolName, StringComparison.Ordinal))
            {
                return nestedCall.Arguments;
            }
        }

        return null;
    }

    private static bool TryBuildFunctionCall(JsonElement element, out KieResponsesFunctionCall functionCall)
    {
        functionCall = default!;

        var type = element.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString()
            : null;
        if (type is not null &&
            type is not ("function_call" or "tool_call" or "function"))
        {
            return false;
        }

        var name = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString()
            : null;
        var callId =
            element.TryGetProperty("call_id", out var callIdProp) && callIdProp.ValueKind == JsonValueKind.String
                ? callIdProp.GetString()
                : element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : null;

        string? arguments = null;
        if (element.TryGetProperty("arguments", out var argsProp))
        {
            arguments = argsProp.ValueKind == JsonValueKind.String
                ? argsProp.GetString()
                : argsProp.GetRawText();
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

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        functionCall = new KieResponsesFunctionCall(
            callId!,
            name!,
            arguments ?? "{}");
        return true;
    }

    private static string? TryExtractJsonTextFallback(string body)
    {
        var text = (ExtractText(body) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = text.StartsWith("```", StringComparison.Ordinal)
            ? ExtractJsonFromCodeFence(text)
            : text;

        if (!LooksLikeJsonPayload(payload))
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(payload);
            return payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJsonFromCodeFence(string text)
    {
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)];
        }

        var firstBracket = text.IndexOf('[');
        var lastBracket = text.LastIndexOf(']');
        if (firstBracket >= 0 && lastBracket > firstBracket)
        {
            return text[firstBracket..(lastBracket + 1)];
        }

        return text.Trim('`').Trim();
    }

    private static bool LooksLikeJsonPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        return (trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
               (trimmed.StartsWith('[') && trimmed.EndsWith(']'));
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

    private static bool TryReadWrappedKieError(
        string payload,
        out string? message,
        out int? code)
    {
        message = null;
        code = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasOutput = root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array;
            var hasChoices = root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array;
            if (hasOutput || hasChoices)
            {
                return false;
            }

            if (root.TryGetProperty("code", out var codeProp) &&
                codeProp.ValueKind == JsonValueKind.Number &&
                codeProp.TryGetInt32(out var parsedCode))
            {
                code = parsedCode;
            }

            if (root.TryGetProperty("msg", out var msgProp) &&
                msgProp.ValueKind == JsonValueKind.String)
            {
                message = msgProp.GetString();
            }
            else if (root.TryGetProperty("message", out var messageProp) &&
                     messageProp.ValueKind == JsonValueKind.String)
            {
                message = messageProp.GetString();
            }

            return !string.IsNullOrWhiteSpace(message) && code is not null and >= 400;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SummarizeInput(IReadOnlyList<KieResponsesInputItem> input)
    {
        var parts = input.Select((item, index) =>
        {
            var contentPreview = item.Content is null
                ? null
                : string.Join(" | ", item.Content.Select(part =>
                    $"{part.Type}:{Preview(part.Text ?? part.ImageUrl, 240)}"));

            return $"#{index}:type={item.Type ?? "<message>"},role={item.Role ?? "<none>"},name={item.Name ?? "<none>"},callId={item.CallId ?? "<none>"},content={contentPreview ?? "<none>"}";
        });

        return string.Join(" || ", parts);
    }

    private static string SummarizeTools(IReadOnlyList<KieResponsesTool>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return "<none>";
        }

        return string.Join(", ", tools.Select(tool => tool switch
        {
            KieResponsesFunctionTool functionTool => $"{functionTool.Type}:{functionTool.Name}",
            _ => tool.Type
        }));
    }

    private static string SummarizeToolChoice(object? toolChoice)
    {
        return toolChoice switch
        {
            null => "<none>",
            string text => text,
            _ => Preview(JsonSerializer.Serialize(toolChoice, JsonOptions))
        };
    }

    private static string SummarizeFunctionCalls(IReadOnlyList<KieResponsesFunctionCall> calls)
    {
        if (calls.Count == 0)
        {
            return "<none>";
        }

        return string.Join(", ", calls.Select(call =>
            $"{call.Name}(callId={call.CallId},args={Preview(call.Arguments, 240)})"));
    }

    private static string Preview(string? value, int maxLength = 1200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var normalized = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...(truncated,total={normalized.Length})";
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
        public List<object>? Tools { get; set; }

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Strict { get; set; }
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
