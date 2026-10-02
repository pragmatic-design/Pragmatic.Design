namespace Pragmatic.Agent.KV;

/// <summary>
///     No encryption (development only — must be explicitly opted in).
/// </summary>
internal sealed class NoEncryption : IKvSecretProtector
{
    public static readonly NoEncryption Instance = new();

    public string Encrypt(string plaintext) => plaintext;
    public string Decrypt(string ciphertext) => ciphertext;
}
