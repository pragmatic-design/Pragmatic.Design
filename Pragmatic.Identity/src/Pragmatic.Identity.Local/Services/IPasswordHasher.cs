namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Password hashing service for local identity management.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash.</summary>
    bool Verify(string password, string hash);

    /// <summary>
    ///     Returns <see langword="true" /> when <paramref name="hash" /> was produced with a weaker work
    ///     factor than the hasher is currently configured for, so it should be transparently upgraded (see
    ///     <see cref="Verify" />). Adaptive hashes embed their cost; as hardware improves the configured cost
    ///     is raised, and existing hashes are re-hashed on the next successful login to keep pace.
    /// </summary>
    /// <remarks>
    ///     Default implementation returns <see langword="false" /> (never rehash) so external implementers
    ///     that do not support cost upgrades keep their current behaviour.
    /// </remarks>
    bool NeedsRehash(string hash) => false;
}
