namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The condition <c>[SearchAcross]</c> becomes: one value against several columns, joined by
///     <c>||</c>, over the row <c>e</c>.
/// </summary>
/// <remarks>
///     Shared by the grid filter and the query, and by the <c>Apply</c> and <c>ToSpecification</c> of
///     each: a second copy is how the two would drift, and one member without it is a <c>CS1061</c>
///     that keeps the whole type from compiling.
/// </remarks>
internal static class SearchAcrossCondition
{
    /// <summary>
    ///     The disjunction over <paramref name="paths" /> for <paramref name="value" />. Ignoring case,
    ///     both sides are lowered — the shape <c>[Filter(IgnoreCase = true)]</c> uses — and a column is
    ///     tested for null first, since lowering dereferences it wherever the expression runs over
    ///     objects rather than SQL.
    /// </summary>
    public static string Render(IEnumerable<string> paths, string value, bool ignoreCase)
        => string.Join(" || ", paths.Select(path => ignoreCase
            ? $"(e.{path} != null && e.{path}.ToLower().Contains({value}.ToLower()))"
            : $"e.{path}.Contains({value})"));
}
