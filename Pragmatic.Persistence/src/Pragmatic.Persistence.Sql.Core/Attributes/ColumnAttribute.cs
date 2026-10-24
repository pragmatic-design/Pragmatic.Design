namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>The column a property maps to, when it is not the property name.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>PostgreSQL only: the type the parameter is cast to on writes (<c>jsonb</c>), when the column's type does not take the bound value as is.</summary>
    public string? Cast { get; init; }
}
