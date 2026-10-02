namespace Pragmatic.Configuration.Vault;

/// <summary>Options for the HashiCorp Vault secret backend (KV v2 engine).</summary>
public sealed class VaultConfigurationOptions
{
    /// <summary>Vault server address (e.g. <c>https://vault.internal:8200</c>).</summary>
    public string Address { get; set; } = "http://127.0.0.1:8200";

    /// <summary>Token used to authenticate. In production prefer a short-lived/renewable token.</summary>
    public string Token { get; set; } = "";

    /// <summary>Mount point of the KV v2 secrets engine. Default <c>secret</c>.</summary>
    public string MountPoint { get; set; } = "secret";

    /// <summary>
    ///     Optional path prefix under the mount for all Pragmatic secrets (e.g. <c>pragmatic</c>), so they do
    ///     not collide with other secrets in the same engine. Tenant secrets nest under <c>tenants/{id}/</c>.
    /// </summary>
    public string? PathPrefix { get; set; }
}
