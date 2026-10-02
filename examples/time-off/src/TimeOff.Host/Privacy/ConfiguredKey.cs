namespace TimeOff.Host.Privacy;

/// <summary>
///     Reads a 32-byte key from configuration, where it is written in base64.
/// </summary>
/// <remarks>
///     User secrets or the environment in production — never a committed file outside Development, and
///     never the database the keys protect: a key stored beside what it protects protects nothing.
/// </remarks>
internal static class ConfiguredKey
{
    private const int KeyLength = 32;

    public static byte[] Read(IConfiguration configuration, string name)
    {
        var configured = configuration[name]
                         ?? throw new InvalidOperationException($"{name} is not configured.");

        var key = Convert.FromBase64String(configured);
        return key.Length == KeyLength
            ? key
            : throw new InvalidOperationException($"{name} must be {KeyLength} bytes, written in base64; it is {key.Length}.");
    }
}
