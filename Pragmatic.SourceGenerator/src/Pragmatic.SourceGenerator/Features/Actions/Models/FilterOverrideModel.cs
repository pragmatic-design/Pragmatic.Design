using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Captures [WithoutFilter&lt;T&gt;] and [FilterMode] attribute data for code generation.
///     Shared by MutationModel, ActionModel, and EndpointModel.
/// </summary>
internal sealed record FilterOverrideModel
{
    /// <summary>
    ///     Fully qualified entity type names whose filters should be disabled.
    ///     Resolved from [WithoutFilter&lt;TFilter&gt;] by extracting TEntity from IQueryFilter&lt;TEntity&gt;.
    ///     Used as argument to IQueryFilterToggle.Disable(typeof(TEntity)) which matches FilterMap entity keys.
    /// </summary>
    public EquatableArray<string> DisabledEntityTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     EF Core named query filters to lift, resolved from <c>[WithoutFilter&lt;TRule&gt;]</c> where
    ///     the argument is a <c>VisibilityRule&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     A declared rule is installed on the EF model, not in the Pragmatic filter provider, so
    ///     disabling its <c>Type</c> would reach nothing. The opt-out for one is its filter name,
    ///     handed to <c>IgnoreQueryFilters</c> — see <c>VisibilityFilterNaming</c>, which spells the
    ///     name for both sides.
    /// </remarks>
    public EquatableArray<string> LiftedQueryFilterNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The FilterMode override value (int cast of FilterMode enum), or null if not specified.</summary>
    public int? FilterModeOverride { get; init; }

    /// <summary>Whether any filter overrides are configured.</summary>
    public bool HasOverrides => !DisabledEntityTypes.IsDefaultOrEmpty
                                || !LiftedQueryFilterNames.IsDefaultOrEmpty
                                || FilterModeOverride is not null;
}
