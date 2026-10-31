using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;

namespace Pragmatic.Privacy.Engines.Documents;

/// <summary>
///     The subject registry on the documents engine, through the registry's boundary's session: what EF Core's <c>EfCoreSubjectRegistry</c>
///     does, the blind index's uniqueness a document id's (<see cref="SubjectLookupDocument" />).
/// </summary>
/// <remarks>
///     ⚠️ The lookup is written before the subject: an identity is claimed before anything about it is stored, and a failure in between leaves a
///     lookup without its subject, which the next call for that identity completes, never an encrypted identity nothing finds.
/// </remarks>
public sealed class DocSubjectRegistry : ISubjectRegistry
{
    private readonly DocSession _session;
    private readonly SubjectIdentity _identity;
    private readonly TimeProvider _timeProvider;

    internal DocSubjectRegistry(DocSession session, SubjectIdentity identity, TimeProvider timeProvider)
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

        var index = Convert.ToBase64String(await _identity.BlindIndexAsync(subjectType, identifier, ct).ConfigureAwait(false));
        var lookup = await LookupAsync(index, ct).ConfigureAwait(false);
        if (lookup is null)
        {
            // An identity that was erased and comes back gets a NEW reference: recognising it would mean keeping something derived from it.
            lookup = new SubjectLookupDocument { LookupIndex = index, SubjectRef = SubjectLookup.NewReference() };
            try
            {
                await _session.InsertAsync(lookup, ct).ConfigureAwait(false);
            }
            catch (Exception taken) when (taken is MongoWriteException { IsDuplicateKey: true } or CosmosException { IsConflict: true })
            {
                // Another caller claimed the same identity in between: one reference per person, and the winner's is the answer.
                lookup = await LookupAsync(index, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("The subject's lookup was taken and then removed.", taken);
            }
        }

        if (await ByReferenceAsync(lookup.SubjectRef, ct).ConfigureAwait(false) is null)
            await _session.InsertAsync(new SubjectDocument
            {
                SubjectRef = lookup.SubjectRef,
                SubjectType = subjectType,
                Identifier = Convert.ToBase64String(_identity.Encrypt(identifier, lookup.SubjectRef)),
                CreatedAt = _timeProvider.GetUtcNow().UtcTicks,
            }, ct).ConfigureAwait(false);

        return lookup.SubjectRef;
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var index = Convert.ToBase64String(await _identity.BlindIndexAsync(subjectType, identifier, ct).ConfigureAwait(false));
        return (await LookupAsync(index, ct).ConfigureAwait(false))?.SubjectRef;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        // Null for both "never existed" and "erased": after an erasure the reference stops meaning a person to everyone.
        var subject = await ByReferenceAsync(subjectRef, ct).ConfigureAwait(false);
        return subject?.Identifier is { } identifier ? _identity.Decrypt(Convert.FromBase64String(identifier), subject.SubjectRef) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var subject = await ByReferenceAsync(subjectRef, ct).ConfigureAwait(false);
        if (subject is null || subject.ForgottenAt is not null)
            return false;

        // The document survives with no identity, the lookup goes: what is left says the reference existed and was erased, holding nothing
        // that leads back to a person. The identity first: once it is gone, the lookup alone finds a reference that resolves to no one.
        subject.Identifier = null;
        subject.ForgottenAt = _timeProvider.GetUtcNow().UtcTicks;
        await _session.ReplaceAsync(subject, ct).ConfigureAwait(false);
        foreach (var lookup in await _session.Query<SubjectLookupDocument>().Where((l, r) => l.SubjectRef == r, subjectRef).ToListAsync(ct).ConfigureAwait(false))
            await _session.DeleteAsync(lookup, ct).ConfigureAwait(false);
        return true;
    }

    private ValueTask<SubjectLookupDocument?> LookupAsync(string index, CancellationToken ct)
        => _session.Query<SubjectLookupDocument>().FirstOrDefaultAsync((l, i) => l.LookupIndex == i, index, ct);

    private ValueTask<SubjectDocument?> ByReferenceAsync(string subjectRef, CancellationToken ct)
        => _session.Query<SubjectDocument>().FirstOrDefaultAsync((s, r) => s.SubjectRef == r, subjectRef, ct);
}
