using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Emits the [Idempotent] endpoint-filter registration shared by the Endpoint,
///     Mutation, and DomainAction configuration templates.
/// </summary>
internal static class IdempotencyFilterHelper
{
    /// <summary>
    ///     Returns the generated lines registering IdempotencyEndpointFilter on the builder,
    ///     or an empty list when the endpoint is not idempotent (or the verb is safe — PRAG0513).
    /// </summary>
    public static List<string> RenderLines(EndpointModel model, string builderVar)
    {
        var lines = new List<string>();

        if (model.Idempotency is not { } idempotency)
            return lines;

        // Safe verbs are idempotent by definition — PRAG0513 already warned, skip emission.
        if (model.HttpMethod is "Get" or "Head" or "Options")
            return lines;

        var headerLiteral = idempotency.HeaderName is not null
            ? $"\"{StringHelper.CSharpLiteral(idempotency.HeaderName)}\""
            : "null";

        // No hash provider: the filter hashes the raw request body itself. A lambda reading the bound
        // body DTO out of the filter's arguments would find nothing — a generated endpoint is a
        // RequestDelegate and binds its own parameters, so that list is empty and every request would
        // hash alike. Filters run before the endpoint reads the body, so the bytes are still there.
        const string hashProvider = "null";

        lines.Add($"{builderVar}.AddEndpointFilter(new global::Pragmatic.Endpoints.Idempotency.IdempotencyEndpointFilter(");
        lines.Add($"    {headerLiteral},");
        lines.Add($"    {idempotency.DurationSeconds},");
        foreach (var line in hashProvider.Split('\n'))
            lines.Add($"    {line.TrimEnd('\r')}");
        lines.Add("));");

        return lines;
    }
}
