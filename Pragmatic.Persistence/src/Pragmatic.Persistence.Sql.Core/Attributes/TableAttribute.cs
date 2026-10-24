namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>The table an entity maps to, when it is not the type name pluralised; a view with <see cref="ReadOnly" />.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Schema { get; init; }

    /// <summary>True for a view (or any source the application never writes): no writer is generated, and an update or delete through a chain is refused at compile time.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>True for a lookup table whose rows are few by nature: a read of every row is not reported (PRAG3021).</summary>
    public bool Small { get; init; }
}
