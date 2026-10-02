using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a versioned endpoint registration.
///     Works for both DomainAction (ExecuteV2) and Endpoint (HandleAsyncV2) versioning.
///     Contains the method name and the body properties available for that version.
/// </summary>
internal sealed record ActionVersionModel
{
    /// <summary>
    ///     Major version number.
    /// </summary>
    public required int Major { get; init; }

    /// <summary>
    ///     Minor version number.
    /// </summary>
    public required int Minor { get; init; }

    /// <summary>
    ///     Patch version number.
    /// </summary>
    public int Patch { get; init; }

    /// <summary>
    ///     The method name (e.g., "Execute", "ExecuteV2", "HandleAsync", "HandleAsyncV2_1_3").
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    ///     Body properties available for this version (filtered by [SinceVersion]).
    /// </summary>
    public required EquatableArray<BodyPropertyModel> BodyProperties { get; init; }

    /// <summary>
    ///     Version string for Asp.Versioning (e.g., "1.0", "2.1").
    ///     Asp.Versioning.ApiVersion only supports major.minor, so patch is for DTO differentiation only.
    /// </summary>
    public string VersionString => Patch > 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}";

    /// <summary>
    ///     Asp.Versioning-compatible version (major.minor only — patch not supported by Asp.Versioning).
    /// </summary>
    public string AspVersionString => $"{Major}.{Minor}";

    /// <summary>
    ///     Body DTO name suffix for this version (e.g., "V1", "V2", "V2_1_3").
    /// </summary>
    public string BodyDtoSuffix => Patch > 0 ? $"V{Major}_{Minor}_{Patch}"
        : Minor > 0 ? $"V{Major}_{Minor}"
        : $"V{Major}";
}
