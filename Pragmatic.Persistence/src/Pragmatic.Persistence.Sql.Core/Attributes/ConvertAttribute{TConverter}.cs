namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a property: the <see cref="ISqlConverter{TModel, TStore}" /> between the property's type and the column's.
///     The generator reads the converter's store type and emits its two functions in the reader, the binders and the
///     predicates' parameters (<c>o.OccurredAt > since</c> binds <c>ToStore(since)</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ConvertAttribute<TConverter> : Attribute
    where TConverter : class;
