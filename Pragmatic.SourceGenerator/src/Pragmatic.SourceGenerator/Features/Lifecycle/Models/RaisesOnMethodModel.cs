using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Models;

/// <summary>
///     The <c>[Raises&lt;TEvent&gt;]</c> declarations found on a <b>method</b>. Carries the facts the
///     feature needs to decide whether to report PRAG2753 — it describes declarations on a member, not a
///     type, so it has no <c>GeneratorModel</c> base.
/// </summary>
/// <remarks>
///     The model says whether the declaring type is an entity instead of the transform dropping the node:
///     the answer and the diagnostic then live in the same place, the way <c>IsDomainEventSource</c> feeds
///     PRAG2750. A transform that returned nothing here would be one more silent drop, which is exactly
///     what the gate's drop-site ratchet counts.
/// </remarks>
internal sealed record RaisesOnMethodModel
{
    /// <summary>The declaration site as the author reads it: <c>Organization.Suspend()</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>
    ///     Whether the method belongs to an entity — the one place the declaration reads as wired,
    ///     because the class-level form on an entity is. Elsewhere the declaration records intent for the
    ///     host's event graph and is left alone.
    /// </summary>
    public bool DeclaredOnAnEntity { get; init; }

    /// <summary>The declared events' simple names — one diagnostic each, naming the shapes that work.</summary>
    public EquatableArray<string> EventNames { get; init; }

    /// <summary>Location of the method, so the squiggle lands on the declaration and not on the type.</summary>
    public LocationInfo? LocationInfo { get; init; }

    public Microsoft.CodeAnalysis.Location? Location => LocationInfo?.ToLocation();
}
