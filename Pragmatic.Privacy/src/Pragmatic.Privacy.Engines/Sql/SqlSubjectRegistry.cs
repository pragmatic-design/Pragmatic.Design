using System.Data.Common;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Privacy.Engines.Sql;

/// <summary>
///     The subject registry on the Sql engine, through the registry's boundary's session: what EF Core's <c>EfCoreSubjectRegistry</c> does,
///     on the same table. Identities are stored encrypted and found through a keyed blind index.
/// </summary>
public sealed class SqlSubjectRegistry : ISubjectRegistry
{
    private readonly SqlSession _session;
    private readonly SubjectIdentity _identity;
    private readonly TimeProvider _timeProvider;

    internal SqlSubjectRegistry(SqlSession session, SubjectIdentity identity, TimeProvider timeProvider)
    {
        _session = session;
        _identity = identity;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async ValueTask<string> GetOrCreateReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var index = await _identity.BlindIndexAsync(subjectType, identifier, ct).ConfigureAwait(false);
        if (await ByIndexAsync(index, ct).ConfigureAwait(false) is { } existing)
            return existing.SubjectRef;

        // An identity that was erased and comes back gets a NEW reference: recognising it would mean keeping something derived from it.
        var row = new SubjectRow
        {
            SubjectRef = SubjectLookup.NewReference(),
            SubjectType = subjectType,
            LookupIndex = index,
            CreatedAt = _timeProvider.GetUtcNow().UtcTicks,
        };
        row.Identifier = _identity.Encrypt(identifier, row.SubjectRef);

        try
        {
            await _session.InsertAsync(row, ct).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // Another caller registered the same identity in between: the unique index refused the duplicate, which is the outcome wanted (one
            // reference per person). The winner's reference is the answer; any other failure has no winner and stays as it is.
            var winner = await ByIndexAsync(index, ct).ConfigureAwait(false);
            if (winner is null)
                throw;
            return winner.SubjectRef;
        }

        return row.SubjectRef;
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var index = await _identity.BlindIndexAsync(subjectType, identifier, ct).ConfigureAwait(false);
        return (await ByIndexAsync(index, ct).ConfigureAwait(false))?.SubjectRef;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        // Null for both "never existed" and "erased": after an erasure the reference stops meaning a person to everyone.
        var row = await ByReferenceAsync(subjectRef, ct).ConfigureAwait(false);
        return row is null ? null : _identity.Decrypt(row.Identifier, row.SubjectRef);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var row = await ByReferenceAsync(subjectRef, ct).ConfigureAwait(false);
        if (row is null || row.ForgottenAt is not null)
            return false;

        // The row survives with neither the identity nor the index: it says the reference existed and was erased, holding nothing that leads
        // back to a person.
        row.LookupIndex = null;
        row.Identifier = null;
        row.ForgottenAt = _timeProvider.GetUtcNow().UtcTicks;
        await _session.UpdateAsync(row, ct).ConfigureAwait(false);
        return true;
    }

    private ValueTask<SubjectRow?> ByIndexAsync(byte[] index, CancellationToken ct)
        => _session.Query<SubjectRow>().Where((s, i) => s.LookupIndex == i, index).FirstOrDefaultAsync(ct);

    private ValueTask<SubjectRow?> ByReferenceAsync(string subjectRef, CancellationToken ct)
        => _session.Query<SubjectRow>().Where((s, r) => s.SubjectRef == r, subjectRef).FirstOrDefaultAsync(ct);
}
