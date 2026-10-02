namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     What <c>[TransitionsTo&lt;TState&gt;(target)]</c> asks of the generated invoker.
/// </summary>
internal sealed record TransitionModel
{
    /// <summary>The state enum, fully qualified with <c>global::</c>.</summary>
    public required string EnumFullTypeName { get; init; }

    /// <summary>The target member's name, e.g. <c>Confirmed</c>.</summary>
    public required string TargetMember { get; init; }

    /// <summary>The entity property the state machine governs — <c>Status</c> unless declared.</summary>
    public string StatePropertyName { get; init; } = "Status";

    public TransitionTimingValue Timing { get; init; }

    public bool IsConditional { get; init; }

    /// <summary>
    ///     On a domain action, the <c>[LoadEntity]</c> field holding the entity to move; null on a mutation,
    ///     whose entity is the pipeline's own.
    /// </summary>
    public string? EntityFieldName { get; init; }

    /// <summary>The entity's simple name, for messages.</summary>
    public string EntityTypeName { get; init; } = "";

    public TransitionProblem Problem { get; init; }

    /// <summary>For <see cref="TransitionProblem.AmbiguousEntity" />: the candidate fields, comma separated.</summary>
    public string? ProblemDetail { get; init; }

    /// <summary>The attribute's own location, where its diagnostics point.</summary>
    public Core.LocationInfo? AttributeLocation { get; init; }

    /// <summary>The target as C#: <c>global::Ns.ReservationStatus.Confirmed</c>.</summary>
    public string TargetExpression => $"{EnumFullTypeName}.{TargetMember}";

    /// <summary>Whether the generated invoker calls <c>TransitionTo</c> itself.</summary>
    public bool InvokerTransitions => Problem == TransitionProblem.None && Timing != TransitionTimingValue.ByBody;

    /// <summary>Whether the generated invoker checks, after the body, that the state was reached.</summary>
    public bool InvokerChecksTheBody =>
        Problem == TransitionProblem.None && Timing == TransitionTimingValue.ByBody && !IsConditional;
}
