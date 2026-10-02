// Pragmatic.SourceGenerator - Composition - Remote Boundary Model

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Represents a [RemoteBoundary&lt;TModule&gt;] declaration on the host's [Module] class.
///     Instructs the SG to generate HTTP invokers instead of local invokers for this module's actions.
/// </summary>
internal sealed record RemoteBoundaryModel
{
    /// <summary>Fully qualified module type name (e.g., <c>global::Showcase.Billing.BillingModule</c>).</summary>
    public required string ModuleTypeName { get; init; }

    /// <summary>Simple module name (e.g., <c>Billing</c>).</summary>
    public required string ModuleName { get; init; }

    /// <summary>
    ///     Optional base URL override from the attribute.
    ///     When null, resolved from configuration at runtime.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>Assembly name where the module is defined.</summary>
    public string? AssemblyName { get; init; }

    /// <summary>Source location for diagnostic reporting.</summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
}
