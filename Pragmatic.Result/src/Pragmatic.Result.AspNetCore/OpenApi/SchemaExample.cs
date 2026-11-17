using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Pragmatic.Result.AspNetCore.OpenApi;

/// <summary>
///     Puts an example on a schema in the form each target's Microsoft.OpenApi has (#832).
/// </summary>
/// <remarks>
///     Microsoft.OpenApi 3, which ASP.NET Core 11 brings, obsoletes <c>OpenApiSchema.Example</c> for the JSON Schema
///     <c>Examples</c> list; the net10.0 asset keeps Microsoft.OpenApi 2 and writes the document it wrote before.
/// </remarks>
internal static class SchemaExample
{
    public static OpenApiSchema With(OpenApiSchema schema, JsonNode? example)
    {
#if NET11_0_OR_GREATER
        schema.Examples = example is null ? null : [example];
#else
        schema.Example = example;
#endif
        return schema;
    }
}
