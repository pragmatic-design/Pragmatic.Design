namespace Pragmatic.Configuration.Options;

/// <summary>
///     Options controlling multi-tenant configuration resolution behavior.
/// </summary>
public sealed class MultiTenantOptions
{
    /// <summary>Whether multi-tenant resolution is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     When <c>true</c> (default), falls back to base configuration if no tenant-specific value exists.
    /// </summary>
    public bool FallbackToBase { get; set; } = true;
}
