using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WebApi.OpenApi;

/// <summary>
/// Surfaces a clean, discoverable schema for the draft-post-generation request
/// in Scalar / Swagger. Without this transformer the auto-generated schema lists
/// `style` as just `string?`, which makes it invisible in the Scalar form-builder.
/// Here we attach an explicit enum + example body so the field renders as a
/// dropdown with the three valid values.
/// </summary>
internal static class RecommendationsOpenApiTransformers
{
    private const string DraftPostsPathSuffix = "/draft-posts";
    private const string DraftPostsPathPrefix = "api/Ai/recommendations/";

    private const string LazyExample =
        """
        {
          "userPrompt": null,
          "style": "branded"
        }
        """;

    private const string TopicExample =
        """
        {
          "userPrompt": "create content about DJI Osmo Mobile 7",
          "style": "marketing",
          "topK": 6,
          "maxReferenceImages": 3
        }
        """;

    internal static Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath ?? string.Empty;
        var isDraftPostsPost =
            path.StartsWith(DraftPostsPathPrefix, StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(DraftPostsPathSuffix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        if (!isDraftPostsPost)
        {
            return Task.CompletedTask;
        }

        if (operation.RequestBody?.Content is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        if (operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Schema = BuildSchema();
            mediaType.Example = JsonNode.Parse(LazyExample);
        }

        if (operation.RequestBody.Content.TryGetValue("application/*+json", out var extended))
        {
            extended.Schema = BuildSchema();
            extended.Example = JsonNode.Parse(LazyExample);
        }

        // Show both example modes (lazy auto-discovery + explicit topic+style) so a user
        // skimming Scalar sees that omitting userPrompt is supported.
        operation.RequestBody.Content.TryGetValue("application/json", out var withExamples);
        if (withExamples is not null)
        {
            withExamples.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["lazy_user_auto_discovery"] = new OpenApiExample
                {
                    Summary = "Lazy mode — AI picks the topic itself",
                    Description = "Omit userPrompt and the AI auto-discovers the next post by RAG-ing the page profile + past posts and web-searching for trending topics in the brand's pillars. Style defaults to 'branded' if omitted.",
                    Value = JsonNode.Parse(LazyExample),
                },
                ["explicit_topic_marketing"] = new OpenApiExample
                {
                    Summary = "Explicit topic + marketing style",
                    Description = "Provide a topic and a style. 'marketing' renders the brand logo + headline + CTA + contact directly on the image.",
                    Value = JsonNode.Parse(TopicExample),
                },
            };
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema BuildSchema()
    {
        // Enum nodes for the `style` field — Scalar renders these as a dropdown.
        var styleEnum = new List<JsonNode>
        {
            JsonValue.Create("creative")!,
            JsonValue.Create("branded")!,
            JsonValue.Create("marketing")!,
        };

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description =
                "Request to start an AI draft-post generation. Both fields are optional — " +
                "if userPrompt is omitted the AI auto-discovers a topic, if style is omitted " +
                "the default 'branded' is used.",
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["userPrompt"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Description =
                        "Optional. Specific topic the AI should write about. If null/empty, " +
                        "the AI auto-discovers a topic from the page's content pillars and " +
                        "current trends (web search).",
                    Example = JsonValue.Create("create content about DJI Osmo"),
                },
                ["style"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Enum = styleEnum,
                    Default = JsonValue.Create("branded"),
                    Description =
                        "Visual + caption style. " +
                        "'creative' = pure mood / no on-image text. " +
                        "'branded' (DEFAULT) = hero visual + subtle brand mark + optional short headline. " +
                        "'marketing' = full promo flyer with logo + headline + CTA + contact rendered on the image.",
                    Example = JsonValue.Create("branded"),
                },
                ["workspaceId"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Format = "uuid",
                    Description = "Optional workspace scope.",
                },
                ["topK"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer | JsonSchemaType.Null,
                    Description = "RAG top-K (1–20). Default 6.",
                    Example = JsonValue.Create(6),
                },
                ["maxReferenceImages"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer | JsonSchemaType.Null,
                    Description = "Max reference past-post images attached to the LLM (1–4). Default 3.",
                    Example = JsonValue.Create(3),
                },
                ["maxRagPosts"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer | JsonSchemaType.Null,
                    Description = "Max past posts to (re-)index before generating. Default 30.",
                    Example = JsonValue.Create(30),
                },
            },
        };
    }
}
