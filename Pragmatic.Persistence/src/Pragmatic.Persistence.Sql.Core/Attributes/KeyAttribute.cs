namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On the key properties, when the key is not the <c>Id</c> property: one attribute per column of a composite key.
///     Writes match on every key column; navigations, includes and identity need a single-column key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class KeyAttribute : Attribute;
