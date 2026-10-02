using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     A method that carries <c>[Invariant]</c> and that the generated invoker cannot call, with the
///     reason — the payload of PRAG0463.
/// </summary>
/// <remarks>
///     Value-equatable, and the location travels as <see cref="LocationInfo" /> rather than as a
///     <c>Location</c>: this flows through the incremental pipeline inside the mutation's model, and a
///     <c>Location</c> holds a syntax tree, which would keep the whole compilation alive and defeat the
///     caching.
/// </remarks>
internal sealed record UncallableInvariantModel
{
    /// <summary>The method's name, as the message names it.</summary>
    public required string MethodName { get; init; }

    /// <summary>
    ///     Which of the five conditions it fails, in the words an author would use about their own
    ///     method. The reason is the whole value of the diagnostic: "cannot be called" alone sends
    ///     somebody to read the accessibility of a method whose problem is its return type.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>Where the method is declared, so the error lands on it and not on the mutation.</summary>
    public LocationInfo? Location { get; init; }
}
