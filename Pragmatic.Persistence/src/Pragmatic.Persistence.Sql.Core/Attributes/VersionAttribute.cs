namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     The optimistic-concurrency column (<c>int</c> or <c>long</c>): an update or delete carries the version
///     read and matches zero rows when another writer got there first (<see cref="SqlConcurrencyException" />);
///     a successful update increments it, in the row and in the object.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VersionAttribute : Attribute;
