namespace Pragmatic.Gateway;

/// <summary>
///     API key authentication configuration.
///     <para>
///         <b>Security note</b>: <see cref="ValidKeys" /> stores raw API key strings. If this
///         section is written to appsettings.json, environment dumps, or log output the keys will
///         be exposed. <b>Always load these values from a secrets manager</b> (Azure Key Vault,
///         HashiCorp Vault, or a non-committed environment variable file) and never commit them to
///         source control.
///     </para>
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>HTTP header that carries the API key. Default: <c>X-Api-Key</c>.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    ///     Accepted raw API key values.
    ///     <b>Do not store in appsettings.json.</b> Use a secrets manager or env vars.
    /// </summary>
    public List<string> ValidKeys { get; set; } = [];
}
