namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing an API version.
/// </summary>
internal sealed record ApiVersionModel
{
    /// <summary>
    ///     The version string.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    ///     Whether this version is deprecated.
    /// </summary>
    public bool Deprecated { get; init; }

    /// <summary>
    ///     Deprecation message.
    /// </summary>
    public string? DeprecationMessage { get; init; }

    /// <summary>
    ///     Sunset date (ISO 8601).
    /// </summary>
    public string? SunsetDate { get; init; }
}
