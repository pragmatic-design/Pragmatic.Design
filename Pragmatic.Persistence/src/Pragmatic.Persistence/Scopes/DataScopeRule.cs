using System.Linq.Expressions;

namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     Non-generic base for <see cref="DataScopeRule{T}"/>.
///     Used for DI registration and runtime discovery.
/// </summary>
public abstract class DataScopeRule
{
    /// <summary>
    ///     The scope identifier this rule controls (e.g., "team-a", "department-sales").
    ///     Materialized as <c>"scope:{ScopeName}"</c> in entity AccessScopes.
    /// </summary>
    public abstract string ScopeName { get; }

    /// <summary>
    ///     The entity type this rule applies to.
    /// </summary>
    public abstract Type EntityType { get; }

    /// <summary>
    ///     The materialization strategy for this rule.
    /// </summary>
    public virtual ScopeStrategy Strategy => ScopeStrategy.Materialized;
}

/// <summary>
///     Defines a typed data scope rule: an expression that determines which entities
///     belong to a named scope. Used for both materialization (populating AccessScopes)
///     and computed filtering (query-time evaluation).
/// </summary>
/// <typeparam name="T">The entity type this rule applies to.</typeparam>
/// <example>
///     <code>
/// public class TeamAScopeRule : DataScopeRule&lt;Order&gt;
/// {
///     public override string ScopeName => "team-a";
///     public override Expression&lt;Func&lt;Order, bool&gt;&gt; ToExpression()
///         => order => order.TeamId == "team-a";
/// }
///     </code>
/// </example>
public abstract class DataScopeRule<T> : DataScopeRule where T : class
{
    /// <inheritdoc />
    public override Type EntityType => typeof(T);

    /// <summary>
    ///     Returns the expression that determines whether an entity belongs to this scope.
    /// </summary>
    public abstract Expression<Func<T, bool>> ToExpression();
}
