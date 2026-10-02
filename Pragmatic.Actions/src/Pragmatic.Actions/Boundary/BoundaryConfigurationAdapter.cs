namespace Pragmatic.Actions.Boundary;

/// <summary>
///     Adapter to expose generic configuration as non-generic interface.
/// </summary>
internal sealed class BoundaryConfigurationAdapter<TBoundary>(BoundaryConfiguration<TBoundary> configuration)
    : IBoundaryConfiguration
    where TBoundary : IBoundary
{
    public Type BoundaryType => typeof(TBoundary);
    public BoundaryMode Mode => configuration.Mode;
    public string? RemoteBaseUrl => configuration.RemoteBaseUrl;
}
