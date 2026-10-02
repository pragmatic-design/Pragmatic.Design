using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>An aggregate invariant: a parameterless bool method marked <c>[Invariant]</c>.</summary>
internal sealed record InvariantModel
{
    /// <summary>Name of the parameterless bool method to invoke on the entity.</summary>
    public required string MethodName { get; init; }

    /// <summary>
    ///     The navigations this rule's body reads, by name — what a caller must have loaded before the
    ///     rule can answer.
    /// </summary>
    /// <remarks>
    ///     A mutation has written what it loaded, so it checks every rule; an operation that only
    ///     <c>[LoadEntity]</c>-ed a row has loaded what it asked for, and a rule over a navigation it
    ///     left out would read an empty collection. Empty means either "reads only columns"
    ///     or "the body is in another assembly and cannot be read" — the caller must treat the second as
    ///     unknown, which is why the filtering lives where both facts are available.
    /// </remarks>
    public EquatableArray<string> Navigations { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether the body was readable at all. False across assemblies, where <see cref="Navigations" />
    ///     is empty for want of syntax rather than for want of navigations.
    /// </summary>
    public bool BodyWasReadable { get; init; }

    /// <summary>Optional explanatory message from <c>[Invariant("…")]</c>; null uses a default.</summary>
    public string? Message { get; init; }

    /// <summary>
    ///     The translation key the refusal reports, from <c>[Invariant(MessageKey = …)]</c>; null leaves
    ///     it on the key derived from the error's code, which every invariant shares.
    /// </summary>
    /// <remarks>
    ///     It is an error key's <b>base</b> — the localizer reads <c>{key}.title</c> and
    ///     <c>{key}.detail</c> — so it is written as a string and not as a generated <c>TKeys</c>
    ///     constant: in that class a base is a nested type, whose members are those two suffixes.
    /// </remarks>
    public string? MessageKey { get; init; }
}
