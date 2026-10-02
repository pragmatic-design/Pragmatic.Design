using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     The metadata used to write a <see cref="ProblemDetails" /> response.
/// </summary>
/// <remarks>
///     Shared by the binding failures and by every error an action returns: both write the same shape,
///     and both must keep working with the reflection fallback disabled. An error response is the last
///     thing that may fail to serialize — it runs when something has already gone wrong.
/// </remarks>
public static class ProblemDetailsJson
{
    public static JsonTypeInfo<ProblemDetails> TypeInfo(HttpContext context)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        // TryGetTypeInfo, not GetTypeInfo: the latter THROWS when no resolver covers the type, so the
        // fallback below was unreachable and the error path died with "no metadata for ProblemDetails"
        // — an exception about failing to report an exception. The AOT smoke caught it.
        //
        // The app's own options come first, so its naming policy and converters apply. The fallback is
        // this package's own context: an error response must never be the thing that cannot serialize.
        return options.TryGetTypeInfo(typeof(ProblemDetails), out var info) && info is JsonTypeInfo<ProblemDetails> typed
            ? typed
            : Serialization.PragmaticProblemDetailsJsonContext.Default.ProblemDetails;
    }
}
