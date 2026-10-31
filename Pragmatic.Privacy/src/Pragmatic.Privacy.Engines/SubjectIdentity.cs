using System.Text;
using Pragmatic.Cryptography;

namespace Pragmatic.Privacy.Engines;

/// <summary>
///     What both engines' registries do to an identity, as EF Core's <c>EfCoreSubjectRegistry</c> does it: the blind index it is found by, and
///     its encryption under the subject's reference.
/// </summary>
internal sealed class SubjectIdentity(ISecretEncryptor encryptor, ISubjectLookupKeyProvider lookupKeys)
{
    /// <summary>The keyed hash of (type, identity) the registry searches by: nothing searchable about the person is stored.</summary>
    public async ValueTask<byte[]> BlindIndexAsync(string subjectType, string identifier, CancellationToken ct)
    {
        var key = await lookupKeys.GetLookupKeyAsync(ct).ConfigureAwait(false);
        return SubjectLookup.BlindIndex(key, subjectType, identifier);
    }

    /// <summary>
    ///     The identity encrypted with the subject's reference as associated data, so an encrypted identity moved to another subject's record
    ///     does not decrypt.
    /// </summary>
    public byte[] Encrypt(string identifier, string subjectRef) => encryptor.EncryptBytes(Encoding.UTF8.GetBytes(identifier), subjectRef);

    /// <summary>The identity, or null when there is none (forgotten) or it does not decrypt under this reference.</summary>
    public string? Decrypt(byte[]? encrypted, string subjectRef)
        => encrypted is not null && encryptor.TryDecryptBytes(encrypted, out var plain, subjectRef) ? Encoding.UTF8.GetString(plain) : null;
}
