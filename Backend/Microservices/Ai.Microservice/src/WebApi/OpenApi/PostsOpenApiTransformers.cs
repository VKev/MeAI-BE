using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WebApi.OpenApi;

internal static class PostsOpenApiTransformers
{
    private const string PublishPath = "api/Ai/posts/publish";

    private const string PublishExample =
        """
        [
          {
            "postId": "11111111-1111-1111-1111-111111111111",
            "socialMediaIds": [
              "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
            ],
            "isPrivate": false
          },
          {
            "postId": "22222222-2222-2222-2222-222222222222",
            "socialMediaIds": [
              "cccccccc-cccc-cccc-cccc-cccccccccccc"
            ]
          }
        ]
        """;

    internal static Task TransformAsync(
        Microsoft.OpenApi.OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Description.RelativePath, PublishPath, StringComparison.OrdinalIgnoreCase) ||
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
            mediaType.Schema = BuildPublishSchema();
            mediaType.Example = JsonNode.Parse(PublishExample);
        }

        if (operation.RequestBody.Content.TryGetValue("application/*+json", out var extendedJsonMediaType))
        {
            extendedJsonMediaType.Schema = BuildPublishSchema();
            extendedJsonMediaType.Example = JsonNode.Parse(PublishExample);
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema BuildPublishSchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["postId"] = (IOpenApiSchema)new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "uuid"
                    },
                    ["socialMediaIds"] = (IOpenApiSchema)new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "uuid"
                        }
                    },
                    ["isPrivate"] = (IOpenApiSchema)new OpenApiSchema
                    {
                        Type = JsonSchemaType.Boolean
                    }
                },
                Required = new HashSet<string>
                {
                    "postId",
                    "socialMediaIds"
                }
            }
        };
    }
}
