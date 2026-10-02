using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing response caching configuration.
/// </summary>
internal sealed record ResponseCacheModel
{
    /// <summary>
    ///     Cache duration in seconds.
    /// </summary>
    public int Duration { get; init; }

    /// <summary>
    ///     Cache location (Any, Client, None).
    /// </summary>
    public string Location { get; init; } = "Any";

    /// <summary>
    ///     Whether to disable caching.
    /// </summary>
    public bool NoStore { get; init; }

    /// <summary>
    ///     Query keys to vary by.
    /// </summary>
    public EquatableArray<string> VaryByQueryKeys { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Headers to vary by.
    /// </summary>
    public EquatableArray<string> VaryByHeaders { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Named cache profile.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    ///     Whether the response is kept by the server for any caller — the case the handlers turn into
    ///     <c>CacheOutput(…)</c>, and the one that needs the host's output cache to do anything.
    ///     <c>Client</c> is a header, <c>None</c> and <c>NoStore</c> forbid keeping it.
    /// </summary>
    public bool IsShared => !NoStore && (Location ?? "Any") is not ("None" or "Client");
}
