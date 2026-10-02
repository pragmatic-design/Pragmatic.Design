// Pragmatic.SourceGenerator - Composition - UsePackage Model

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Represents a [UsePackage&lt;T&gt;] declaration on a module class.
///     The package's metadata (actions, services, entities) are fused into the module.
/// </summary>
internal sealed record UsePackageModel
{
    /// <summary>
    ///     Fully qualified type name of the package definition (T in [UsePackage&lt;T&gt;]).
    /// </summary>
    public required string PackageTypeName { get; init; }

    /// <summary>
    ///     Assembly name where the package is defined.
    /// </summary>
    public required string PackageAssemblyName { get; init; }

    /// <summary>
    ///     Route prefix for package endpoints. Resolved as: attribute override ?? package default ?? null.
    /// </summary>
    public string? RoutePrefix { get; init; }

    /// <summary>
    ///     Location for diagnostics.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
}
