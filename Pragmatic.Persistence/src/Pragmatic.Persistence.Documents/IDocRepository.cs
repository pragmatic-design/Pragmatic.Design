namespace Pragmatic.Persistence.Documents;

/// <summary>A repository of the documents engine: the unit of work it reads and writes through, which a compiled bulk write runs on.</summary>
public interface IDocRepository
{
    DocUnitOfWork UnitOfWork { get; }
}
