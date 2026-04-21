using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WebApi.OpenApi;

internal static class AiGenerationOpenApiTransformers
{
    private const string PreparePath = "api/AiGeneration/post-prepare";
    private const string CaptionsPath = "api/AiGeneration/captions";

    private const string PrepareExample =
        """
        {
          "workspaceId": "11111111-1111-1111-1111-111111111111",
          "resourceIds": [
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
          ],
          "socialMedia": [
            {
              "platform": "facebook",
              "type": "posts"
            },
            {
              "platform": "tiktok",
              "type": "reels",
              "resourceIds": [
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
              ]
            }
          ]
        }
        """;

    private const string CaptionsExample =
        """
        {
          "language": "English",
          "instruction": "Keep each caption concise and platform-native.",
          "socialMedia": [
            {
              "postId": "11111111-1111-1111-1111-111111111111",
              "platform": "tiktok",
              "resourceIds": [
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
              ]
            },
            {
              "postId": "22222222-2222-2222-2222-222222222222",
              "platform": "facebook",
              "resourceIds": [
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
              ]
            },
            {
              "postId": "33333333-3333-3333-3333-333333333333",
              "platform": "ig",
              "resourceIds": [
                "cccccccc-cccc-cccc-cccc-cccccccccccc"
              ]
            },
            {
              "postId": "44444444-4444-4444-4444-444444444444",
              "platform": "threads",
              "resourceIds": [
                "dddddddd-dddd-dddd-dddd-dddddddddddd"
              ]
            }
          ]
        }
        """;

    internal static Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (string.Equals(context.Description.RelativePath, PreparePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            if (operation.RequestBody?.Content is not { Count: > 0 })
            {
                return Task.CompletedTask;
            }

            if (operation.RequestBody.Content.TryGetValue("application/json", out var prepareMediaType))
            {
                prepareMediaType.Example = JsonNode.Parse(PrepareExample);
            }

            if (operation.RequestBody.Content.TryGetValue("application/*+json", out var prepareExtendedMediaType))
            {
                prepareExtendedMediaType.Example = JsonNode.Parse(PrepareExample);
            }

            return Task.CompletedTask;
        }

        if (!string.Equals(context.Description.RelativePath, CaptionsPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (operation.RequestBody?.Content is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        if (operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Example = JsonNode.Parse(CaptionsExample);
        }

        return Task.CompletedTask;
    }
}
