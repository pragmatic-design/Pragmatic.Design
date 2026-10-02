using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     The <c>ValidateLoaded()</c> / <c>ValidateLoadedAsync(CancellationToken)</c> an operation declares:
///     the rules that read what its invoker preloaded, called right after the preload.
/// </summary>
internal sealed record LoadedValidationModel
{
    /// <summary>An operation that declares neither.</summary>
    public static readonly LoadedValidationModel None = new();

    /// <summary>Whether <c>ValidationError ValidateLoaded()</c> is declared.</summary>
    public bool Sync { get; init; }

    /// <summary>Whether <c>Task&lt;ValidationError&gt; ValidateLoadedAsync(CancellationToken)</c> (or <c>ValueTask</c>) is declared.</summary>
    public bool Async { get; init; }

    /// <summary>A method of either name with another shape — PRAG0452 — which the invoker would not call.</summary>
    public EquatableArray<string> Misshapen { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Where the first misshapen method is declared, for PRAG0452.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>Whether the invoker calls anything.</summary>
    public bool IsDeclared => Sync || Async;
}
