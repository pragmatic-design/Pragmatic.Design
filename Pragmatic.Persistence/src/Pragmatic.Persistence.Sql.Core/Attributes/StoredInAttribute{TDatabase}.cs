namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>The database an entity's table lives in, when the assembly declares more than one <see cref="SqlDatabaseAttribute" />.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoredInAttribute<TDatabase> : Attribute where TDatabase : class;
