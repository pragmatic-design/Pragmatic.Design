using Pragmatic.Persistence.Documents;

namespace Pragmatic.Jobs.Engines.Documents;

/// <summary>
///     The session of the <c>[EnableJobPersistence]</c> boundary, as the job store reads it (<see cref="Sql.SqlJobStoreSession" />'s twin): a
///     holder rather than the session itself, so the scope disposes the session once, under its own key.
/// </summary>
internal sealed record DocJobStoreSession(DocSession Session);
