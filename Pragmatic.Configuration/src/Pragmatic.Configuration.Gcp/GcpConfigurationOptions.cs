namespace Pragmatic.Configuration.Gcp;

/// <summary>Options for the GCP Secret Manager backend.</summary>
public sealed class GcpConfigurationOptions
{
    /// <summary>GCP project id that owns the secrets. Required.</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>
    ///     Optional prefix for all Pragmatic secret ids (e.g. <c>pragmatic</c>). Secret ids are flat in Secret
    ///     Manager, so the prefix, tenant, and key are joined with <c>-</c>.
    /// </summary>
    public string? Prefix { get; set; }
}
