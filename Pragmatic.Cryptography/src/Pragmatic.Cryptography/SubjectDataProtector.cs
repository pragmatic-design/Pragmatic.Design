using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Cryptography;

/// <summary>
///     Default <see cref="ISubjectDataProtector" />: encrypts under the subject's key and, on the way
///     back, consults the key store so an erased value is reported as erased rather than as a failure.
/// </summary>
/// <remarks>
///     A cipher is built per operation rather than cached. Correct first: caching per-subject keys is a
///     real need but it introduces the failure mode that matters most here — a key still in the cache
///     after it was destroyed keeps erased data readable, and nothing would report it.
/// </remarks>
public sealed class SubjectDataProtector(IKeyResolver resolver, ISubjectKeyStore store) : ISubjectDataProtector
{
    public async ValueTask<byte[]> ProtectAsync(
        string subjectRef, byte[] plain, string? associatedData = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentNullException.ThrowIfNull(plain);

        var key = await store.GetOrCreateAsync(subjectRef, ct).ConfigureAwait(false);
        var aad = associatedData is null ? [] : Encoding.UTF8.GetBytes(associatedData);

        using var cipher = new AesGcm(key.Material, CiphertextHeader.TagSize);
        return AesGcmPacker.Pack(cipher, key.KeyId, plain, aad);
    }

    public async ValueTask<SubjectReadResult> TryReadAsync(
        byte[] packed, string? associatedData = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packed);

        // No header means the value was not written by this protector. It is not "erased" — we have no
        // key id to check against — so it can only be reported as a failure to authenticate.
        if (!CiphertextHeader.TryReadVersioned(packed, out var keyId, out var body))
            return SubjectReadResult.Failed;

        var status = await store.GetStatusByKeyIdAsync(keyId, ct).ConfigureAwait(false);

        // Ask before decrypting, not after. A destroyed key fails the tag check exactly like a tampered
        // value, so deciding on the decrypt result alone would collapse the two states we split apart.
        if (status == SubjectKeyStatus.Destroyed)
            return SubjectReadResult.Erased;

        if (status == SubjectKeyStatus.Unknown)
            return SubjectReadResult.Failed;

        var key = await resolver.FindByIdAsync(keyId, ct).ConfigureAwait(false);
        if (key is null)
        {
            // The key was live a moment ago and is not resolvable now — it was destroyed in between.
            // Re-reading the status is what keeps a concurrent erasure from being reported as an attack.
            var recheck = await store.GetStatusByKeyIdAsync(keyId, ct).ConfigureAwait(false);
            return recheck == SubjectKeyStatus.Destroyed ? SubjectReadResult.Erased : SubjectReadResult.Failed;
        }

        var aad = associatedData is null ? [] : Encoding.UTF8.GetBytes(associatedData);

        using var cipher = new AesGcm(key.Material, CiphertextHeader.TagSize);
        return AesGcmPacker.TryUnpackFramed(cipher, body, aad, out var plain)
            ? new SubjectReadResult(DecryptOutcome.Success, plain)
            : SubjectReadResult.Failed;
    }
}
