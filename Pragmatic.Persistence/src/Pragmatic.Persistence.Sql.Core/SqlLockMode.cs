namespace Pragmatic.Persistence.Sql;

/// <summary>How a <c>ForUpdate</c> chain treats rows another transaction already holds.</summary>
public enum SqlLockMode
{
    /// <summary>Wait for them (the database's lock timeout applies).</summary>
    Wait = 0,

    /// <summary>Leave them out: <c>SKIP LOCKED</c> / <c>READPAST</c>, the shape of a work queue.</summary>
    SkipLocked = 1,

    /// <summary>Fail at once: <c>NOWAIT</c>.</summary>
    NoWait = 2,
}
