namespace Infrastructure.Configs;

public sealed class N8nOptions
{
    public const string SectionName = "N8n";

    public string BaseUrl { get; set; } = "http://nginx:5678/n8n";

    public string ScheduledAgentJobPath { get; set; } = "/webhook/meai/scheduled-agent-job";

    public string WebSearchPath { get; set; } = "/webhook/meai/web-search";

    public string CallbackBaseUrl { get; set; } = "http://api-gateway:8080";

    public string RuntimeCallbackPath { get; set; } = "/api/Ai/internal/agent-schedules/runtime-result";

    public string InternalCallbackToken { get; set; } = "change-me";
}
