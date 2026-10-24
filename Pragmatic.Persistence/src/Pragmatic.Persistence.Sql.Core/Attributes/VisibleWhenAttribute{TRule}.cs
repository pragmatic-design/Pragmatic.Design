namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On an entity: a rule every read applies (soft delete, active only), declared once. The rule's
///     <see cref="VisibilityRule{T}.ToExpression" /> is translated at compile time and appended to every
///     plan on the entity; <c>WithoutFilters()</c> on a chain reads past it. Same shape as the repository's.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class VisibleWhenAttribute<TRule> : Attribute
    where TRule : class;
