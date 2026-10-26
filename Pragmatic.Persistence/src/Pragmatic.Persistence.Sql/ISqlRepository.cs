namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A repository of the Sql engine, over a unit of work: what a bulk write intercepted where it is called (<see cref="SqlBulk" />) runs its
///     statement on, the session of the repository's unit of work, stamped by it.
/// </summary>
public interface ISqlRepository
{
    /// <summary>The unit of work the repository reads and writes through.</summary>
    SqlUnitOfWork UnitOfWork { get; }
}
