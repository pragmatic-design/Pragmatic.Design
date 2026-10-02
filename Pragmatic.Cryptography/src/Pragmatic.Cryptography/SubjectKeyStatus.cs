namespace Pragmatic.Cryptography;

/// <summary>
///     What the key store knows about a key id found in a ciphertext header.
/// </summary>
public enum SubjectKeyStatus
{
    /// <summary>No record of this key id. Either it was never issued here, or the value is not ours.</summary>
    Unknown = 0,

    /// <summary>The key exists and can decrypt.</summary>
    Live = 1,

    /// <summary>
    ///     The key was destroyed. Data encrypted under it is permanently unreadable — by design, not by
    ///     failure.
    /// </summary>
    Destroyed = 2
}
