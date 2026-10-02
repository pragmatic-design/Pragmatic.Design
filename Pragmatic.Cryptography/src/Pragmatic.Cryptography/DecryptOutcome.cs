namespace Pragmatic.Cryptography;

/// <summary>
///     The three ways reading protected data can end.
/// </summary>
/// <remarks>
///     Two states would not be enough. Once keys can be destroyed, every legitimate read of erased data
///     fails to decrypt — and if that is reported the same way as a bad tag, then either erased records
///     look like an attack, or a real attack disappears into the noise of ordinary erasures. Which of the
///     two you get is decided by how many subjects have been erased, which is not a good way to run a
///     security signal.
/// </remarks>
public enum DecryptOutcome
{
    /// <summary>The value was decrypted.</summary>
    Success = 0,

    /// <summary>
    ///     The key that wrote this value has been destroyed. Expected, and not a security event: it is
    ///     what erasure looks like from the read side.
    /// </summary>
    KeyDestroyed = 1,

    /// <summary>
    ///     Authentication failed: the value was tampered with, the associated data does not match, or the
    ///     key id belongs to no key this deployment ever issued. <b>This one is a security signal.</b>
    /// </summary>
    AuthenticationFailed = 2
}
