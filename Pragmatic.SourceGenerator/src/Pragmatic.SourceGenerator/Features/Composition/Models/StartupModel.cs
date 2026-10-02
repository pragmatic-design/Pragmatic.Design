// Pragmatic.SourceGenerator - Composition - Startup Model

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Immutable model for an IStartupStep implementation.
/// </summary>
internal sealed record StartupModel
{
    /// <summary>Gets the namespace of the startup class.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the type name of the startup class.</summary>
    public required string TypeName { get; init; }

    /// <summary>Gets the fully qualified type name.</summary>
    public required string FullTypeName { get; init; }

    /// <summary>Gets the priority value.</summary>
    public required int Priority { get; init; }

    /// <summary>Gets the accessibility modifier.</summary>
    public required string Accessibility { get; init; }

    /// <summary>Gets the configuration sections required by this module (from [RequiresConfig] attributes).</summary>
    public EquatableArray<string> RequiredConfigSections { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Location for diagnostic reporting.</summary>
    public LocationInfo? LocationInfo { get; init; }

    /// <summary>If set, the step is invalid and must not be generated (only reported).</summary>
    public InvalidReason InvalidReason { get; init; } = InvalidReason.None;
    public bool IsValid => InvalidReason == InvalidReason.None;
}
