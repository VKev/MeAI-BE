using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WebApi.OpenApi;

internal static class GeminiOpenApiTransformers
{
    private const string PreparePostsPath = "api/Gemini/post-prepare";
    private const string CaptionsPath = "api/Gemini/captions";

    private const string PreparePostsExample =
        """
        {
          "workspaceId": "11111111-1111-1111-1111-111111111111",
          "postType": "posts",
          "language": "English",
          "instruction": "Keep the captions product-focused and platform-native",
          "socialMedia": [
            {
              "type": "Tiktok",
              "resourceList": [
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
              ]
            },
            {
              "type": "Facebook",
              "resourceList": [
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
              ]
            },
            {
              "type": "IG",
              "resourceList": [
                "cccccccc-cccc-cccc-cccc-cccccccccccc"
              ]
            },
            {
              "type": "Threads",
              "resourceList": [
                "dddddddd-dddd-dddd-dddd-dddddddddddd"
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
              "socialMediaType": "TikTok",
              "resourceList": [
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
              ]
            },
            {
              "postId": "22222222-2222-2222-2222-222222222222",
              "socialMediaType": "Facebook",
              "resourceList": [
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
              ]
            },
            {
              "postId": "33333333-3333-3333-3333-333333333333",
              "socialMediaType": "IG",
              "resourceList": [
                "cccccccc-cccc-cccc-cccc-cccccccccccc"
              ]
            },
            {
              "postId": "44444444-4444-4444-4444-444444444444",
              "socialMediaType": "Threads",
              "resourceList": [
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
        if (!string.Equals(context.Description.RelativePath, PreparePostsPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(context.Description.RelativePath, CaptionsPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
        }

        var example = string.Equals(context.Description.RelativePath, PreparePostsPath, StringComparison.OrdinalIgnoreCase)
            ? PreparePostsExample
            : CaptionsExample;

        if (string.Equals(context.Description.RelativePath, PreparePostsPath, StringComparison.OrdinalIgnoreCase))
        {
            operation.Summary = null;
            operation.Description = null;
        }

        if (operation.RequestBody?.Content is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        if (operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Example = JsonNode.Parse(example);
        }

        return Task.CompletedTask;
    }
}
