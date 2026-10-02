using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     What the compile-time document becomes when a host sets
///     <c>PragmaticEndpointsOptions.EnableOpenApi = false</c>.
/// </summary>
/// <remarks>
///     <para>
///         The compile-time document describes the Pragmatic endpoints and nothing else, so leaving them
///         out leaves no operations: <c>paths</c> is empty, and the tags and schemas that existed only
///         to describe those operations go with them. The document itself is still served — its
///         <c>info</c> and security schemes — so a route that fetches it does not start answering 404.
///     </para>
///     <para>
///         Parsed and re-serialised here, unlike the security composition, because removal cannot be
///         done by insertion. It runs once per process, on a document this generator wrote; a document
///         that is not a JSON object comes back unchanged.
///     </para>
/// </remarks>
internal static class PragmaticOpenApiDocument
{
    public static string WithoutOperations(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject document)
            return json;

        document["paths"] = new JsonObject();
        document.Remove("tags");

        if (document["components"] is JsonObject components)
            components.Remove("schemas");

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
