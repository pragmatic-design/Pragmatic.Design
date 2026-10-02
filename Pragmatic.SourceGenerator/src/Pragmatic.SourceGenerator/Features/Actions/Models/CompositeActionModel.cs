using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Represents a [CompositeAction] [DomainAction] that orchestrates multiple mutations
///     within a single transaction.
/// </summary>
internal sealed record CompositeActionModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string Accessibility { get; init; }
    public bool IsVoid { get; init; }
    public string? ReturnTypeName { get; init; }
    public string? BelongsToTypeName { get; init; }
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
    public bool IsValid { get; init; } = true;

    /// <summary>
    ///     The composite declared <c>[Transactional]</c>: one database transaction around the steps,
    ///     each of which saves so the next can read what it wrote.
    /// </summary>
    public bool IsTransactional { get; init; }

    /// <summary>
    ///     Whether this composite answers for the permissions its steps declare.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same attribute a mutation uses for its nested children, and now the same default:
    ///         a permission on a step holds, and the composite has to say when it answers instead.
    ///         Before, a composite absorbed them <b>always</b> and silently — a
    ///         <c>[RequirePermission]</c> written on a mutation stopped meaning anything the moment
    ///         somebody used that mutation as a step.
    ///     </para>
    ///     <para>
    ///         ⚠️ The two shapes of composition sat a step apart with opposite defaults, and the one
    ///         that surprised was the silent one. Which of the two is right is a product decision;
    ///         that they disagree without saying so was not.
    ///     </para>
    /// </remarks>
    public bool AbsorbsChildPermissions { get; init; }

    /// <summary>
    ///     The author wrote an <c>Execute</c> of their own, so the generator must not write one.
    /// </summary>
    /// <remarks>
    ///     A composite's body <b>is</b> its steps, so the base class's abstract <c>Execute</c> has
    ///     nothing to hold. Making the author write <c>=&gt; Task.FromResult(Success())</c> to satisfy
    ///     the compiler puts a method in their file that says "this does nothing and succeeds" — false,
    ///     and false in the one place a reader looks first. The generator writes it instead.
    /// </remarks>
    public bool DeclaresExecute { get; init; }

    /// <summary>
    ///     The composite declared <c>[CommitStrategy(CommitMode.PerStep)]</c>, which contradicts what a
    ///     composite is. Carried so the feature can report PRAG0430 rather than ignore it.
    /// </summary>
    public bool DeclaresPerStep { get; init; }

    /// <summary>
    ///     The composite carries <c>[Endpoint]</c>, so a caller can reach it over HTTP.
    /// </summary>
    public bool IsExposed { get; init; }

    /// <summary>
    ///     The composite says who may run it — <c>[RequirePermission]</c>, <c>[RequirePolicy&lt;T&gt;]</c>
    ///     or the explicit <c>[AllowAnonymous]</c>.
    /// </summary>
    public bool DeclaresAuthorization { get; init; }

    /// <summary>
    ///     Steps that require a permission of their own, which the composite suppresses.
    /// </summary>
    /// <remarks>
    ///     They run as internal calls, so their <c>[RequirePermission]</c> is deliberately not
    ///     re-checked — the composite is the authorization boundary. Carried so the feature can report
    ///     PRAG0440 when the composite is reachable and says nothing about who may reach it.
    /// </remarks>
    public EquatableArray<string> StepsRequiringPermission { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The mutations this composite action orchestrates.
    ///     Derived from properties of type Mutation&lt;T&gt; on the action class.
    /// </summary>
    public EquatableArray<CompositeStepModel> Steps { get; init; } = EquatableArray<CompositeStepModel>.Empty;

    public bool HasSteps => !Steps.IsDefaultOrEmpty;

    public EquatableArray<DependencyModel> Dependencies { get; init; } = EquatableArray<DependencyModel>.Empty;
    public bool HasDependencies => !Dependencies.IsDefaultOrEmpty;
}

/// <summary>What a composite step is, which decides how the generated invoker calls it.</summary>
internal enum CompositeStepKind
{
    /// <summary>A <c>Mutation&lt;TEntity&gt;</c>.</summary>
    Mutation = 0,

    /// <summary>A <c>DomainAction&lt;TReturn&gt;</c>.</summary>
    Action = 1,

    /// <summary>A <c>VoidDomainAction</c>.</summary>
    VoidAction = 2
}

/// <summary>
///     A single step in a composite action.
/// </summary>
/// <remarks>
///     Mutations and actions are both steps. They were not: the transform recognised only properties
///     deriving from <c>Mutation&lt;T&gt;</c>, so a composite of actions produced an empty step list,
///     no invoker, and — because the documented convention leaves the body empty — an action that did
///     nothing at all, without a diagnostic.
/// </remarks>
internal sealed record CompositeStepModel
{
    /// <summary>The property name on the composite action (e.g., "CreateReservation").</summary>
    public required string PropertyName { get; init; }

    /// <summary>Whether this step is a mutation, an action, or a void action.</summary>
    public required CompositeStepKind Kind { get; init; }

    /// <summary>The step's full type name (e.g., "global::Contoso.Sales.CreateReservationMutation").</summary>
    public required string StepFullTypeName { get; init; }

    /// <summary>The step's short type name.</summary>
    public required string StepTypeName { get; init; }

    /// <summary>
    ///     What the step returns: the entity for a mutation, the return type for an action, <c>null</c>
    ///     for a void action.
    /// </summary>
    public string? ResultFullTypeName { get; init; }

    /// <summary>The invoker type the composite injects for this step.</summary>
    public required string InvokerFullTypeName { get; init; }
}
