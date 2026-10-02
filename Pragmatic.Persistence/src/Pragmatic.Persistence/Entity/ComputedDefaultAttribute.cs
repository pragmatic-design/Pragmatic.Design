namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Specifies a computed default value for an entity property, resolved at creation time
///     via an <see cref="Lifecycle.IDefaultValueGenerator{TEntity,TValue}" />.
/// </summary>
/// <example>
///     <code>
/// [ComputedDefault&lt;Invoice, string, InvoiceNumberGenerator&gt;]
/// public string InvoiceNumber { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ComputedDefaultAttribute<TEntity, TValue, TGenerator> : Attribute
    where TEntity : class
    where TGenerator : Lifecycle.IDefaultValueGenerator<TEntity, TValue>;
