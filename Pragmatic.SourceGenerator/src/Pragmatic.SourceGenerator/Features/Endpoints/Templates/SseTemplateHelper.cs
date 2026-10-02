using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Emits the SSE handling shared by the Endpoint and DomainAction templates:
///     the ToSseResult invocation (first-item peek + stream writer) and the local
///     __AdaptSse function mapping each Result into an SseStreamEvent.
/// </summary>
internal static class SseTemplateHelper
{
    /// <summary>The SseStreamOptions expression for the endpoint (heartbeat), or "null".</summary>
    public static string OptionsExpression(EndpointModel model)
        => model.SseHeartbeatSeconds is > 0
            ? $"new global::Pragmatic.Endpoints.Configuration.SseStreamOptions {{ HeartbeatSeconds = {model.SseHeartbeatSeconds} }}"
            : "null";

    /// <summary>
    ///     Lines for a standalone StreamingEndpoint handler body:
    ///     invoke HandleAsync, adapt, and hand over to ToSseResult.
    /// </summary>
    public static List<string> RenderStandaloneLines(EndpointModel model)
    {
        var itemType = model.StreamItemTypeName!;
        var resultType = model.ErrorTypes.IsDefaultOrEmpty
            ? $"global::Pragmatic.Result.Result<{itemType}>"
            : $"global::Pragmatic.Result.Result<{itemType}, {string.Join(", ", model.ErrorTypes.Select(e => e.TypeName))}>";

        var lines = new List<string>
        {
            "var __stream = endpoint.HandleAsync(ct);",
            "return await global::Pragmatic.Endpoints.Sse.SseResultExtensions.ToSseResult(",
            "    __AdaptSse(__stream, ct), httpContext,",
            $"    {OptionsExpression(model)}, ct);",
            ""
        };

        lines.AddRange(RenderAdapterLines(model, itemType, resultType, TypedMatchHandlers(model, itemType)));
        return lines;
    }

    /// <summary>
    ///     Lines for a StreamingDomainAction handler body: the invoker result carries the
    ///     stream (filters already ran pre-stream); failures map to ProblemDetails.
    /// </summary>
    public static List<string> RenderDomainActionLines(EndpointModel model)
    {
        var itemType = model.StreamItemTypeName!;
        var resultType = $"global::Pragmatic.Result.Result<{itemType}, global::Pragmatic.Result.IError>";

        var lines = new List<string>
        {
            "return await result.Match(",
            "    (__streamValue) => global::Pragmatic.Endpoints.Sse.SseResultExtensions.ToSseResult(",
            "        __AdaptSse(__streamValue, ct), httpContext,",
            $"        {OptionsExpression(model)}, ct),",
            "    (__error) => global::System.Threading.Tasks.Task.FromResult(",
            "        global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(__error, httpContext)));",
            ""
        };

        lines.AddRange(RenderAdapterLines(model, itemType, resultType,
            [
                $"(__item) => global::Pragmatic.Endpoints.Responses.SseStreamEvent<{itemType}>.FromItem(__item)",
                $"(__error) => global::Pragmatic.Endpoints.Responses.SseStreamEvent<{itemType}>.FromError(__error)"
            ]));
        return lines;
    }

    private static List<string> TypedMatchHandlers(EndpointModel model, string itemType)
    {
        var handlers = new List<string>
        {
            $"(__item) => global::Pragmatic.Endpoints.Responses.SseStreamEvent<{itemType}>.FromItem(__item)"
        };

        if (model.ErrorTypes.IsDefaultOrEmpty)
            handlers.Add(
                $"(__error) => global::Pragmatic.Endpoints.Responses.SseStreamEvent<{itemType}>.FromError(__error)");
        else
            handlers.AddRange(model.ErrorTypes.Select(e =>
                $"({e.TypeName} __error) => global::Pragmatic.Endpoints.Responses.SseStreamEvent<{itemType}>.FromError(__error)"));

        return handlers;
    }

    private static List<string> RenderAdapterLines(
        EndpointModel model, string itemType, string resultType, List<string> matchHandlers)
    {
        var lines = new List<string>
        {
            $"static async global::System.Collections.Generic.IAsyncEnumerable<global::Pragmatic.Endpoints.Responses.SseStreamEvent<{itemType}>> __AdaptSse(",
            $"    global::System.Collections.Generic.IAsyncEnumerable<{resultType}> source,",
            "    [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken ct = default)",
            "{",
            "    await foreach (var __r in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation(source, ct))",
            "        yield return __r.Match("
        };

        for (var i = 0; i < matchHandlers.Count; i++)
        {
            var comma = i < matchHandlers.Count - 1 ? "," : ");";
            lines.Add($"            {matchHandlers[i]}{comma}");
        }

        lines.Add("}");
        return lines;
    }
}
