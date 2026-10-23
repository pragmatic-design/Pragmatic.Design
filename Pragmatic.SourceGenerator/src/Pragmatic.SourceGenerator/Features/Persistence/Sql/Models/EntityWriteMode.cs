namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>How a map writes its entity's rows.</summary>
internal enum EntityWriteMode
{
    /// <summary>Through its writer.</summary>
    Writer,

    /// <summary>A view: the session refuses the write.</summary>
    ReadOnly,

    /// <summary>A table-per-concrete-type root: each row through its concrete type's map.</summary>
    Dispatch,

    /// <summary>A table-per-type entity: every write through a batch, the derived table's statement beside the root's.</summary>
    DispatchWithWriter,
}
