namespace Pragmatic.Cryptography;

/// <summary>
///     The result of reading subject-protected data.
/// </summary>
/// <param name="Outcome">Which of the three outcomes occurred.</param>
/// <param name="Plain">The decrypted bytes when <paramref name="Outcome"/> is
///     <see cref="DecryptOutcome.Success"/>; empty otherwise.</param>
public readonly record struct SubjectReadResult(DecryptOutcome Outcome, byte[] Plain)
{
    /// <summary>True when the value was decrypted.</summary>
    public bool IsSuccess => Outcome == DecryptOutcome.Success;

    /// <summary>An erased value: expected, and not a security event.</summary>
    public static SubjectReadResult Erased { get; } = new(DecryptOutcome.KeyDestroyed, []);

    /// <summary>A value that did not authenticate. <b>This is a security signal.</b></summary>
    public static SubjectReadResult Failed { get; } = new(DecryptOutcome.AuthenticationFailed, []);
}
