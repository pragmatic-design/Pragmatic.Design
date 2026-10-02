using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Model representing a domain event handler to be registered.
/// </summary>
internal sealed record EventHandlerModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    /// <summary>
    ///     Every domain event this class handles, fully qualified — one entry per
    ///     <c>IDomainEventHandler&lt;T&gt;</c> it implements.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Every interface, not the <b>first</b> one found: a class that handles three and is
    ///     registered for one leaves the other two events with no handler at all — valid C#, a green
    ///     build, and silence.
    /// </remarks>
    public required EquatableArray<string> EventTypeFullNames { get; init; }
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>If set, the handler is invalid and must not be generated (only reported).</summary>
    public InvalidReason InvalidReason { get; init; } = InvalidReason.None;
    public bool IsValid => InvalidReason == InvalidReason.None;
}
