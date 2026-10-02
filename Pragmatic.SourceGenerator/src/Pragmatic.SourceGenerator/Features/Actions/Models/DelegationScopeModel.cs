namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     The delegation an action opens around its own execution, from
///     <c>[StartsDelegation(nameof(…))]</c>.
/// </summary>
/// <remarks>
///     The subject is a property on the action rather than a value here: it is known per invocation,
///     not per compilation. The generator only needs its name, and to have checked that it exists and
///     is a string — <c>PRAG0423</c> otherwise.
/// </remarks>
internal sealed record DelegationScopeModel
{
    /// <summary>The action property holding the subject's id.</summary>
    public required string SubjectPropertyName { get; init; }

    /// <summary>Reaches the audit trail; null when the author did not say.</summary>
    public string? Purpose { get; init; }

    /// <summary>The <c>DelegationPolicy</c> value, as its underlying int.</summary>
    public int Policy { get; init; }
}
