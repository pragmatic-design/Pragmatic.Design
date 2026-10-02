namespace Pragmatic.Configuration.Discovery;

/// <summary>
///     Everything the application declares it can be configured with, across every assembly the host
///     composes.
/// </summary>
/// <remarks>
///     Filled by the source generator: each assembly contributes the <c>[Configuration]</c> types it
///     declares, and the host aggregates them. Nothing is discovered by reflection, so the catalogue is
///     complete under Native AOT and is known before the application runs — which is what makes
///     <see cref="ConfigurationPreflight" /> possible.
/// </remarks>
public interface IConfigurationCatalog
{
    /// <summary>The declared sections, in no particular order.</summary>
    IReadOnlyList<ConfigurationSectionDescriptor> Sections { get; }
}
