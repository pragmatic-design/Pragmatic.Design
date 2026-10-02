namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The name a declared <c>[VisibleWhen&lt;TRule&gt;]</c> rule is installed under as an EF Core
///     named global query filter.
/// </summary>
/// <remarks>
///     <para>
///         Two generators spell this name, and they must agree exactly or the query throws: the entity
///         configuration writes it into <c>HasQueryFilter(name, …)</c>, and every handler carrying
///         <c>[WithoutFilter&lt;TRule&gt;]</c> writes it into <c>DisableQueryFilter(name)</c>. One
///         function, so there is nothing to keep in sync.
///     </para>
///     <para>
///         The fully qualified type name is the whole name rather than the simple one, because a
///         filter name has to be unique across the model: two rules called <c>ActiveOnly</c> in two
///         boundaries are ordinary, and EF would take the second registration as a redefinition of
///         the first. Nobody types the result, so its length costs nothing.
///     </para>
/// </remarks>
internal static class VisibilityFilterNaming
{
    private const string GlobalPrefix = "global::";

    /// <summary>The prefix that marks a query filter as a declared visibility rule.</summary>
    public const string Prefix = "Visibility:";

    /// <summary>
    ///     Builds the EF Core filter name for a rule type.
    /// </summary>
    /// <param name="fullyQualifiedRuleType">
    ///     The rule type as the transforms record it, with or without the <c>global::</c> prefix.
    /// </param>
    public static string ForRule(string fullyQualifiedRuleType)
    {
        var bare = fullyQualifiedRuleType.StartsWith(GlobalPrefix, System.StringComparison.Ordinal)
            ? fullyQualifiedRuleType.Substring(GlobalPrefix.Length)
            : fullyQualifiedRuleType;

        return Prefix + bare;
    }
}
