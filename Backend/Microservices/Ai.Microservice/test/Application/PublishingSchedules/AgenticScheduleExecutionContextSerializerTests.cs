using System.Text.Json;
using Application.Abstractions.Automation;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Models;
using FluentAssertions;

namespace AiMicroservice.Tests.Application.PublishingSchedules;

public sealed class AgenticScheduleExecutionContextSerializerTests
{
    [Fact]
    public void Serialize_ShouldRemoveJsonbUnsupportedCharacters_FromNestedSearchPayload()
    {
        var context = new AgenticScheduleExecutionContext(
            Search: new PublishingScheduleSearchInput("AI\0 query", 5, "VN", "vi\u001f", "pd"),
            LastSearchPayload: new AgentWebSearchResponse(
                "AI\0 query",
                DateTime.UtcNow,
                [
                    new AgentWebSearchResultItem(
                        "Title\0",
                        "https://example.com/article\0",
                        "Description\u001f",
                        "search",
                        "Page title",
                        "\u001f\uFFFD\b\u0000compressed bytes",
                        ["https://example.com/image.jpg\0"])
                ],
                "context\0",
                [
                    new ImportedResourceItem(
                        Guid.NewGuid(),
                        "https://cdn.example.com/image.jpg\0",
                        "image/jpeg",
                        "image",
                        "https://example.com/image.jpg\0",
                        "https://example.com/article")
                ]),
            CurrentStep: "web_search\0",
            Steps:
            [
                new AgenticExecutionProgressLog(
                    "web_search",
                    "Completed",
                    "done\0",
                    DateTime.UtcNow)
            ]);

        var json = AgenticScheduleExecutionContextSerializer.Serialize(context);

        json.Should().NotContain("\\u0000");
        using var document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        var parsed = AgenticScheduleExecutionContextSerializer.Parse(json);
        parsed.Search!.QueryTemplate.Should().NotContain("\0");
        parsed.LastSearchPayload!.Query.Should().NotContain("\0");
        parsed.LastSearchPayload.Results[0].PageContent.Should().NotContain("\0");
        parsed.LastSearchPayload.Results[0].PageContent.Should().NotContain("\u001f");
        parsed.LastSearchPayload.Results[0].PageContent.Should().NotContain("\b");
        parsed.LastSearchPayload.ImportedResources![0].SourceUrl.Should().NotContain("\0");
        parsed.Steps![0].Message.Should().NotContain("\0");
    }
}
