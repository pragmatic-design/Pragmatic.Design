namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A unique index over the entity's columns, by property name (the repository's <c>[Unique("PropertyId", "Code")]</c>), in the schema the
///     generator emits for Pragmatic.Migrations. <see cref="Filter" /> makes it partial (<c>"ValidTo" IS NULL</c>: one open row per parent); a
///     [SoftDelete] entity's index holds its live rows only, so a deleted row does not block the next one. Named <c>IX_{Table}_{Columns}</c>
///     unless <see cref="Name" /> says otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UniqueAttribute(params string[] properties) : Attribute
{
    public string[] Properties { get; } = properties;

    /// <summary>The partial index's predicate, double-quoted identifiers; a boolean literal as <c>{bt:0}</c>/<c>{bt:1}</c>, each provider's own.</summary>
    public string? Filter { get; init; }

    public string? Name { get; init; }
}
