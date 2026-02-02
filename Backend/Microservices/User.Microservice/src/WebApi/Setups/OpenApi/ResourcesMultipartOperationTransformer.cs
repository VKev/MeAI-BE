using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WebApi.Setups.OpenApi;

public sealed class ResourcesMultipartOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var relativePath = context.Description.RelativePath ?? string.Empty;
        var method = context.Description.HttpMethod ?? string.Empty;

        var isResourcesEndpoint = relativePath.StartsWith("api/User/resources", StringComparison.OrdinalIgnoreCase);
        var isAvatarEndpoint = relativePath.Equals("api/User/profile/avatar", StringComparison.OrdinalIgnoreCase);

        if (!isResourcesEndpoint && !isAvatarEndpoint)
        {
            return Task.CompletedTask;
        }

        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPut(method))
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = BuildMultipartSchema()
                }
            }
        };

        return Task.CompletedTask;
    }

    private static OpenApiSchema BuildMultipartSchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["file"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "binary"
                },
                ["status"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                },
                ["resourceType"] = (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                }
            },
            Required = new HashSet<string> { "file" }
        };
    }
}
