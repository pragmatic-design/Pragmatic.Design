// Pragmatic.Discovery - Discovery Options

namespace Pragmatic.Discovery.Options;

/// <summary>
/// Configuration options for the Pragmatic Discovery service.
/// Configure via <c>services.AddDiscovery(opts => ...)</c>.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>
    /// If true, the host automatically registers its own topology at startup
    /// by reading the <c>[assembly: PragmaticMetadata(HostTopology, ...)]</c> attribute.
    /// Default: true.
    /// </summary>
    public bool AutoRegisterOnStartup { get; set; } = true;

    /// <summary>
    /// If true, runs cross-host coherence validation after registration.
    /// Issues (module conflicts, ReadAccess coherence) are logged or thrown
    /// depending on <see cref="ThrowOnValidationFailure"/>.
    /// Default: true.
    /// </summary>
    public bool ValidateOnStartup { get; set; } = true;

    /// <summary>
    /// If true, throws <see cref="InvalidOperationException"/> when validation
    /// finds Error-severity issues. Warnings are always logged only.
    /// Default: false (log-only in dev; recommended true in staging/prod).
    /// </summary>
    public bool ThrowOnValidationFailure { get; set; } = false;

    /// <summary>
    /// Name of the section in <c>appsettings.json</c> where Discovery options live.
    /// Used by <c>services.Configure&lt;DiscoveryOptions&gt;(config.GetSection(SectionName))</c>.
    /// </summary>
    public const string SectionName = "Pragmatic:Discovery";
}
