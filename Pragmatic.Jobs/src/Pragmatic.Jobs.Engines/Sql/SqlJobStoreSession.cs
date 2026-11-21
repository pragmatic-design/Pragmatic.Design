using Pragmatic.Persistence.Sql;

namespace Pragmatic.Jobs.Engines.Sql;

/// <summary>
///     The session of the <c>[EnableJobPersistence]</c> boundary, as the job store reads it: every other registration of a boundary is keyed by
///     the boundary, and the store, chosen by the host, does not know which boundary that is. A holder rather than the session itself, so the
///     scope disposes the session once, under its own key.
/// </summary>
internal sealed record SqlJobStoreSession(SqlSession Session);
