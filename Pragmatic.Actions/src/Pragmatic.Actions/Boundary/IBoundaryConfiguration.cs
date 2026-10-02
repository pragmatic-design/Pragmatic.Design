namespace Pragmatic.Actions.Boundary;

/// <summary>
///     Non-generic view of a boundary configuration, as <c>GetAllBoundaryConfigurations()</c> lists them.
/// </summary>
public interface IBoundaryConfiguration
{
    /// <summary>
    ///     Gets the boundary type.
    /// </summary>
    Type BoundaryType { get; }

    /// <summary>
    ///     Gets the boundary mode.
    /// </summary>
    BoundaryMode Mode { get; }

    /// <summary>
    ///     Gets the remote base URL (only for remote boundaries).
    /// </summary>
    string? RemoteBaseUrl { get; }
}
