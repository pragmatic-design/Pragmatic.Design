using Pragmatic.Composition.Attributes;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     BCrypt-based password hasher. Uses the BCrypt work factor for adaptive hashing.
/// </summary>
[Service(Lifetime = Lifetime.Singleton)]
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    /// <param name="workFactor">
    ///     BCrypt cost factor. Must be within BCrypt's supported range (4–31); higher is slower
    ///     and more resistant to brute force. Default: 12.
    /// </param>
    public BcryptPasswordHasher(int workFactor = 12)
    {
        ThrowIfOutOfRange(workFactor, 4, 31);
        _workFactor = workFactor;
    }

    /// <inheritdoc />
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);

    /// <inheritdoc />
    /// <remarks>
    ///     Returns <see langword="false"/> for a null/empty/whitespace stored hash rather than throwing.
    ///     This closes the trap where a freshly-created entity whose <c>PasswordHash</c> still defaults to
    ///     <see cref="string.Empty"/> could otherwise reach the BCrypt verifier with an unset credential.
    /// </remarks>
    public bool Verify(string password, string hash) =>
        !string.IsNullOrWhiteSpace(hash) && BCrypt.Net.BCrypt.EnhancedVerify(password, hash);

    /// <inheritdoc />
    /// <remarks>
    ///     The BCrypt work factor is encoded in the hash prefix — <c>$2a$12$…</c> where <c>12</c> is the
    ///     cost. We parse that cost and compare it to the configured one; a lower stored cost means the hash
    ///     predates a cost increase and should be upgraded. A malformed/empty hash returns
    ///     <see langword="false"/> (nothing to upgrade — never churn on an unparseable value).
    /// </remarks>
    public bool NeedsRehash(string hash) =>
        TryGetWorkFactor(hash, out var cost) && cost < _workFactor;

    // Parses the cost from a BCrypt modular-crypt hash: $<version>$<cost>$<22-char salt><31-char hash>.
    private static bool TryGetWorkFactor(string? hash, out int workFactor)
    {
        workFactor = 0;
        if (string.IsNullOrEmpty(hash) || hash.Length < 7 || hash[0] != '$')
            return false;

        var secondDollar = hash.IndexOf('$', 1);
        if (secondDollar < 0 || secondDollar + 3 > hash.Length || hash[secondDollar + 3] != '$')
            return false;

        var costSpan = hash.AsSpan(secondDollar + 1, 2);
        return int.TryParse(costSpan, out workFactor);
    }
}
