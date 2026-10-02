namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Renders a <see cref="QueryStrategyKind" /> as the runtime enum member the generated code names.
/// </summary>
internal static class QueryStrategyKindExtensions
{
    private const string Prefix = "global::Pragmatic.Persistence.Query.QueryStrategy.";

    /// <summary>The fully qualified runtime enum member for this strategy.</summary>
    /// <remarks>
    ///     Written out member by member rather than interpolating the enum's name: the string a
    ///     generated file contains is then greppable from here, and a member renamed on either side
    ///     fails to compile instead of emitting a name that does not exist.
    /// </remarks>
    public static string ToRuntimeConstant(this QueryStrategyKind strategy) => strategy switch
    {
        QueryStrategyKind.Projection => Prefix + "Projection",
        QueryStrategyKind.Entity => Prefix + "Entity",
        QueryStrategyKind.Raw => Prefix + "Raw",
        _ => Prefix + "Filtered"
    };
}
