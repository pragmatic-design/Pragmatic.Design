namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>A property the table does not have (a navigation, a computed value).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NotMappedAttribute : Attribute;
