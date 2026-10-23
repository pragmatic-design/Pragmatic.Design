namespace Pragmatic.Persistence.Entity;

/// <summary>
///     The persistence engines a boundary can name with <see cref="PersistenceEngineAttribute" />.
/// </summary>
/// <remarks>
///     The numbers are what the source generator reads from the attribute, so they do not change.
/// </remarks>
public enum PersistenceEngineKind
{
    /// <summary>EF Core, through <c>Pragmatic.Persistence.EFCore</c>.</summary>
    EfCore = 0,

    /// <summary>The Sql engine.</summary>
    Sql = 1,

    /// <summary>The documents engine (MongoDB, Cosmos DB).</summary>
    Documents = 2
}
