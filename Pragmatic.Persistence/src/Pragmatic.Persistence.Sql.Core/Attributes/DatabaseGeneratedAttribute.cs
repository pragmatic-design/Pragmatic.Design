namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A column the database fills (a default, a computed column, a trigger): never written by inserts, updates or upserts, and read back by
///     key after an insert, and after an update too with <see cref="OnUpdate" /> (a computed column over the others, a modification stamp).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DatabaseGeneratedAttribute : Attribute
{
    public bool OnUpdate { get; init; }
}
