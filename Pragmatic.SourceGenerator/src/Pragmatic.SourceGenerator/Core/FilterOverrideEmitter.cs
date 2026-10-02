using System.Collections.Generic;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The <c>using</c> scopes that put <c>[WithoutFilter&lt;T&gt;]</c> and <c>[FilterMode]</c> into
///     effect around a load.
/// </summary>
/// <remarks>
///     <para>
///         Five templates emit these lines — two invokers and three endpoint handlers — and for a
///         while three shared this helper while two kept their own copy, which is how the two copies
///         came to differ from the three. They differ in one thing only, the expression naming the
///         toggle, so that is a parameter and the rest is said once.
///     </para>
/// </remarks>
internal static class FilterOverrideEmitter
{
    /// <summary>
    ///     Yields the scope lines to emit, in order.
    /// </summary>
    /// <param name="overrides">The parsed attributes.</param>
    /// <param name="toggle">
    ///     The expression naming the toggle at the emission site — <c>filterToggle</c> where it is a
    ///     bound handler parameter, <c>_filterToggle?</c> where it is an optional field.
    /// </param>
    public static IEnumerable<string> ScopeLines(FilterOverrideModel overrides, string toggle)
    {
        for (var i = 0; i < overrides.DisabledEntityTypes.Length; i++)
            yield return
                $"using var __disableFilter{i} = {toggle}.Disable(typeof({overrides.DisabledEntityTypes[i]}));";

        for (var i = 0; i < overrides.LiftedQueryFilterNames.Length; i++)
            yield return
                $"using var __liftFilter{i} = {toggle}.DisableQueryFilter(\"{overrides.LiftedQueryFilterNames[i]}\");";

        if (overrides.FilterModeOverride is not null)
            yield return
                $"using var __filterMode = {toggle}.UseMode((global::Pragmatic.Persistence.Query.Filters.FilterMode){overrides.FilterModeOverride.Value});";
    }
}
