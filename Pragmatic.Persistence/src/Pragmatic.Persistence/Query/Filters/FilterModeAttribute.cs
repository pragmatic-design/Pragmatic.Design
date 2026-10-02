namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Declares that the annotated action, mutation, or endpoint should execute under a specific <see cref="FilterMode"/>.
///     The mode is set via <see cref="IQueryFilterToggle.UseMode(FilterMode)"/> before execution.
/// </summary>
/// <example>
///     <code>
/// [Mutation(Mode = MutationMode.Update)]
/// [FilterMode(FilterMode.Admin)]
/// public partial class AdminBulkUpdateMutation : Mutation&lt;Invoice&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FilterModeAttribute(FilterMode mode) : Attribute
{
    /// <summary>
    ///     The filter mode to activate during execution.
    /// </summary>
    public FilterMode Mode { get; } = mode;
}
