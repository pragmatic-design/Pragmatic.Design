using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     The name a declared <c>[VisibleWhen&lt;TRule&gt;]</c> rule is installed under as an EF Core
///     named global query filter.
/// </summary>
/// <remarks>
///     <para>
///         The generator writes this name twice — into <c>HasQueryFilter</c> in the entity
///         configuration, and into the opt-out of every handler carrying
///         <c>[WithoutFilter&lt;TRule&gt;]</c> — from a single function of its own. This is the same
///         function for code written by hand, so the two never drift: a name EF does not know throws
///         when the query runs, and it throws at the caller rather than quietly returning every row.
///     </para>
///     <para>
///         The fully qualified name is the whole name, not the simple one, because a filter name must
///         be unique across the model and two boundaries may each have an <c>ActiveOnly</c>. Nested
///         types are normalised from the CLR's <c>+</c> to <c>.</c> so both sides spell one string.
///     </para>
/// </remarks>
public static class VisibilityFilterName
{
    /// <summary>The prefix marking a query filter as a declared visibility rule.</summary>
    public const string Prefix = "Visibility:";

    /// <summary>
    ///     The filter name for a rule type.
    /// </summary>
    /// <typeparam name="TRule">The <c>VisibilityRule&lt;T&gt;</c> declared on the entity.</typeparam>
    public static string Of<TRule>() where TRule : class => Of(typeof(TRule));

    /// <summary>
    ///     The filter name for a rule type.
    /// </summary>
    /// <param name="ruleType">The rule type.</param>
    public static string Of(Type ruleType)
    {
        ThrowIfNull(ruleType);

        return Prefix + (ruleType.FullName ?? ruleType.Name).Replace('+', '.');
    }
}
