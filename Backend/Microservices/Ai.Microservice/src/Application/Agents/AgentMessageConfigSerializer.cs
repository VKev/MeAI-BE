using System.Text.Json;
using Application.Agents.Models;
using Domain.Entities;

namespace Application.Agents;

public static class AgentMessageConfigSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(AgentChatMetadata metadata)
    {
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    public static AgentChatMetadata Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AgentChatMetadata(Role: "user");
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<AgentChatMetadata>(json, JsonOptions);
            return metadata is null || string.IsNullOrWhiteSpace(metadata.Role)
                ? new AgentChatMetadata(Role: "user")
                : metadata;
        }
        catch (JsonException)
        {
            return new AgentChatMetadata(Role: "user");
        }
    }

    public static AgentMessageResponse ToResponse(Chat chat)
    {
        var metadata = Parse(chat.Config);
        return new AgentMessageResponse(
            chat.Id,
            chat.SessionId,
            metadata.Role,
            chat.Prompt,
            chat.Status,
            chat.ErrorMessage,
            metadata.Model,
            metadata.ToolNames?.ToArray() ?? Array.Empty<string>(),
            metadata.Actions?.ToArray() ?? Array.Empty<AgentActionResponse>(),
            chat.CreatedAt,
            chat.UpdatedAt);
    }
}
