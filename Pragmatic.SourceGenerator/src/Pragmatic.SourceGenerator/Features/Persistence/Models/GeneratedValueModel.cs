using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for an <c>[GeneratedValue]</c> property that opts into auto-generation. Drives the
///     generated <c>IDefaultValueGenerator</c> that formats the value at entity-creation time.
/// </summary>
/// <remarks>
///     Only app-side formats (date / <c>{RANDOM:N}</c> / <c>{GUID:N}</c>) are modelled here; formats
///     containing a <c>{SEQ:N}</c> database-sequence token are handled separately and are excluded by the
///     transform, so this model never carries a sequence segment.
/// </remarks>
internal sealed record GeneratedValueModel
{
    /// <summary>Fully-qualified entity type (without the <c>global::</c> prefix).</summary>
    public required string EntityFullName { get; init; }

    /// <summary>Entity type short name (e.g. <c>Order</c>).</summary>
    public required string EntityShortName { get; init; }

    /// <summary>Entity namespace (may be empty for the global namespace).</summary>
    public required string Namespace { get; init; }

    /// <summary>The property whose value is generated (e.g. <c>OrderNumber</c>).</summary>
    public required string PropertyName { get; init; }

    /// <summary>Name of the generated default-value generator class.</summary>
    public required string GeneratorClassName { get; init; }

    /// <summary>
    ///     How the value is written back: the property itself when its setter is public, otherwise the
    ///     generated <c>Set{Property}</c> method.
    /// </summary>
    /// <remarks>
    ///     The binding registered for the save pipeline writes through a delegate, and a delegate
    ///     cannot assign a private setter. Deciding here rather than in the template keeps the two
    ///     sides that never meet — the declaration and the registration — reading the same answer.
    /// </remarks>
    public required string SetterName { get; init; }

    /// <summary>The parsed format segments.</summary>
    public required EquatableArray<GeneratedValueSegment> Segments { get; init; }

    /// <summary>True when the format contains a <c>{SEQ:N}</c> token (needs a DB sequence).</summary>
    public bool HasSequence { get; init; }

    /// <summary>The database sequence name (default <c>{Entity}_{Property}_seq</c>). Set only when <see cref="HasSequence"/>.</summary>
    public string? SequenceName { get; init; }

    /// <summary>
    ///     FQN of the entity's boundary, used to inject the correct keyed <c>DbContext</c> in host mode
    ///     for the sequence round-trip. Null when no boundary is resolvable (console mode falls back to
    ///     the non-keyed DbContext).
    /// </summary>
    public string? TargetBoundaryFullName { get; init; }
}
