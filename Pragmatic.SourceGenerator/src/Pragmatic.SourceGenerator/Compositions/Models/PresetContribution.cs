using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Compositions.Models;

/// <summary>
///     Contribution for entities with [HasPresets] + [PresetProvider&lt;T&gt;] attributes.
///     Contains the list of preset providers to call during entity creation.
/// </summary>
internal sealed record PresetContribution
{
    /// <summary>Preset providers ordered by their Order property. EquatableArray (not raw
    /// ImmutableArray) keeps this model value-equatable so it doesn't break incremental caching.</summary>
    public required EquatableArray<PresetProviderModel> Providers { get; init; }
}

/// <summary>
///     A single preset provider declared via [PresetProvider&lt;T&gt;].
/// </summary>
internal sealed record PresetProviderModel
{
    /// <summary>The FQN of the provider type (e.g., "global::MyApp.ReservationPresetProvider").</summary>
    public required string ProviderTypeFqn { get; init; }

    /// <summary>Execution order (lower = first).</summary>
    public int Order { get; init; }
}
