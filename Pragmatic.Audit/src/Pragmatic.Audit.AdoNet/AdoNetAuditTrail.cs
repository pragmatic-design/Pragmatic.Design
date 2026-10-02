using System.Data;
using System.Data.Common;

namespace Pragmatic.Audit.AdoNet;

/// <summary>
///     Writes trail entries with raw ADO.NET, on a connection and transaction the caller owns.
/// </summary>
/// <remarks>
///     <para>
///         Exists because the EF writer cannot reach every producer that needs atomicity. A producer
///         built on ADO.NET with its own dialect over several providers — the configuration store is
///         exactly that — cannot hand the EF writer a <c>DbContext</c> on its connection without taking
///         a dependency on EF <em>and</em> on one specific provider, in a package written to abstract
///         over providers. That was discovered by measuring, after the enlisting contract was already
///         built for it.
///     </para>
///     <para>
///         Entry shaping goes through <see cref="AuditEntryPreparer" />, the same as every other
///         writer. Duplicating it here would eventually mean an entry with no segment — unsealable, and
///         therefore unverifiable — or an unredacted detail.
///     </para>
/// </remarks>
public sealed class AdoNetAuditTrail(IAuditSqlDialect dialect, AuditEntryPreparer preparer)
{
    /// <summary>
    ///     Appends an entry on the caller's transaction, so it commits with whatever else is in it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The entry's segment is already sealed. Refused rather than absorbed: it would sit outside the
    ///     Merkle root and the next verification would call it tampering.
    /// </exception>
    public async Task RecordAsync(
        AuditEntry entry, DbConnection connection, DbTransaction? transaction = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        preparer.Prepare(entry);

        await EnsureSegmentOpenAsync(entry, connection, transaction, ct).ConfigureAwait(false);
        await InsertAsync(entry, connection, transaction, ct).ConfigureAwait(false);
    }

    private async Task EnsureSegmentOpenAsync(
        AuditEntry entry, DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        var check = connection.CreateCommand();
        await using (check.ConfigureAwait(false))
        {
            check.Transaction = transaction;
            check.CommandText = dialect.SelectSegmentSealed;
            Add(check, "@segmentId", entry.SegmentId);

            var sealedAt = await check.ExecuteScalarAsync(ct).ConfigureAwait(false);

            if (sealedAt is not null && sealedAt != DBNull.Value)
                throw new InvalidOperationException(
                    $"Segment '{entry.SegmentId}' is already sealed; an entry timestamped " +
                    $"{entry.OccurredAt:O} cannot be appended to it. This means a write outlived the " +
                    "sealing grace period.");

            // Absent is the ordinary case for the first write of an hour, and is not an error.
            if (sealedAt is null)
                await OpenSegmentAsync(entry, connection, transaction, ct).ConfigureAwait(false);
        }
    }

    private async Task OpenSegmentAsync(
        AuditEntry entry, DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        var open = connection.CreateCommand();
        await using (open.ConfigureAwait(false))
        {
            open.Transaction = transaction;
            open.CommandText = dialect.UpsertSegment;
            Add(open, "@segmentId", entry.SegmentId);
            Add(open, "@openedAt", entry.OccurredAt.UtcTicks);

            try
            {
                await open.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (DbException) when (transaction is null)
            {
                // Two writers opening the same segment at the same instant is the normal case, not an
                // edge one. The dialects insert-if-absent, but a provider that loses the race reports a
                // duplicate key — and the row we wanted now exists, which is the outcome we wanted.
                // Only swallowed outside a transaction: inside one, the failure has already poisoned it
                // and hiding that would commit an entry into a segment that was never opened.
            }
        }
    }

    private async Task InsertAsync(
        AuditEntry entry, DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        var insert = connection.CreateCommand();
        await using (insert.ConfigureAwait(false))
        {
            insert.Transaction = transaction;
            insert.CommandText = dialect.InsertEntry;

            Add(insert, "@segmentId", entry.SegmentId);
            // Ticks, matching the value converter the EF store uses: a trail written by two paths must
            // read back the same either way, and DateTimeOffset is not comparable across providers.
            Add(insert, "@occurredAt", entry.OccurredAt.UtcTicks);
            Add(insert, "@category", (int)entry.Category);
            Add(insert, "@operation", entry.Operation);
            Add(insert, "@actorRef", entry.ActorRef);
            Add(insert, "@subjectRef", entry.SubjectRef);
            Add(insert, "@tenantId", entry.TenantId);
            Add(insert, "@correlationId", entry.CorrelationId);
            Add(insert, "@businessOperation", entry.BusinessOperation);
            Add(insert, "@onBehalfOfRef", entry.OnBehalfOfRef);
            Add(insert, "@targetType", entry.TargetType);
            Add(insert, "@targetId", entry.TargetId);
            Add(insert, "@outcome", (int)entry.Outcome);
            Add(insert, "@valueHash", entry.ValueHash);
            Add(insert, "@detail", entry.Detail);

            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static void Add(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
