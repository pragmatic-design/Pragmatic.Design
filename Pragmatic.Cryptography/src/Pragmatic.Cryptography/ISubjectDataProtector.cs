namespace Pragmatic.Cryptography;

/// <summary>
///     Encrypts and decrypts data under a subject's own key, so destroying that key erases the data
///     everywhere it exists — including in backups no <c>DELETE</c> can reach.
/// </summary>
public interface ISubjectDataProtector
{
    /// <summary>
    ///     Encrypts <paramref name="plain" /> under the subject's key, creating that key on first use.
    /// </summary>
    /// <exception cref="SubjectKeyDestroyedException">
    ///     The subject's key was destroyed. Writing new data for an erased subject is refused rather than
    ///     silently starting a fresh key.
    /// </exception>
    ValueTask<byte[]> ProtectAsync(
        string subjectRef, byte[] plain, string? associatedData = null, CancellationToken ct = default);

    /// <summary>
    ///     Reads a value produced by <see cref="ProtectAsync" />, reporting which of the three outcomes
    ///     occurred rather than a bare success flag.
    /// </summary>
    /// <remarks>
    ///     The subject is not a parameter: the key id travels in the value's header, which is what lets a
    ///     caller decrypt a row without knowing whose it is.
    /// </remarks>
    ValueTask<SubjectReadResult> TryReadAsync(
        byte[] packed, string? associatedData = null, CancellationToken ct = default);
}
