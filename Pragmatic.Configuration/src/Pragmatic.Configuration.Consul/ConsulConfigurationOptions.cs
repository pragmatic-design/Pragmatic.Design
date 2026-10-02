namespace Pragmatic.Configuration.Consul;

/// <summary>Options for the HashiCorp Consul KV configuration backend.</summary>
public sealed class ConsulConfigurationOptions
{
    /// <summary>Consul agent HTTP address (e.g. <c>http://127.0.0.1:8500</c>).</summary>
    public string Address { get; set; } = "http://127.0.0.1:8500";

    /// <summary>Optional ACL token.</summary>
    public string? Token { get; set; }

    /// <summary>
    ///     Optional key prefix under which all Pragmatic entries live (e.g. <c>pragmatic</c>). Tenant entries
    ///     nest under <c>tenants/{id}/</c>.
    /// </summary>
    public string? KeyPrefix { get; set; }
}
