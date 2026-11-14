namespace Pragmatic.Persistence.Query;

/// <summary>Where the rows whose sort key is null go in a sort (<c>[Sort(Nulls = …)]</c>, #733), whatever the direction.</summary>
public enum NullOrder
{
    /// <summary>
    ///     The database's own order, which differs between them: PostgreSQL and Oracle put nulls last ascending, SQL Server, MySQL and SQLite
    ///     first.
    /// </summary>
    Default,

    /// <summary>The rows without a value first.</summary>
    First,

    /// <summary>The rows without a value last.</summary>
    Last
}
