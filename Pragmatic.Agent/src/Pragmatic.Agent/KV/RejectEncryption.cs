namespace Pragmatic.Agent.KV;

/// <summary>
///     Reject all secret operations (fail-closed when no encryption key is configured).
/// </summary>
internal sealed class RejectEncryption : IKvSecretProtector
{
    public static readonly RejectEncryption Instance = new();

    public string Encrypt(string plaintext) =>
        throw new InvalidOperationException("Cannot store secrets: no encryption key configured. Set PRAGMATIC_AGENT_SECRET_KEY or create secret.key file.");

    public string Decrypt(string ciphertext) =>
        throw new InvalidOperationException("Cannot read secrets: no encryption key configured. Set PRAGMATIC_AGENT_SECRET_KEY or create secret.key file.");
}
