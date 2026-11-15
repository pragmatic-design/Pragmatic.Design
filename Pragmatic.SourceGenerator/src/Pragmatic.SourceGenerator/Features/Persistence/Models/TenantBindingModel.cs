using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One <c>[FromTenant]</c> property of an operation: whether the invoker can write the request's
///     tenant into it, and what it does when there is none.
/// </summary>
internal sealed record TenantBindingModel
{
    /// <summary>The operation property the invoker writes.</summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     Whether the property is a <c>string?</c>: with no tenant resolved it is left null, where a
    ///     <c>string</c> one refuses the operation.
    /// </summary>
    public bool AcceptsNoTenant { get; init; }

    /// <summary>Why the binding cannot be written — the tail of PRAG0747 — or null when it can.</summary>
    public string? Problem { get; init; }

    /// <summary>Where the property is declared, for PRAG0747.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>Whether the invoker writes the binding.</summary>
    public bool IsRendered => Problem is null;
}
