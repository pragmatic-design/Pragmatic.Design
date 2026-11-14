using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Compositions.Models;

namespace Pragmatic.SourceGenerator.Tests.Compositions;

/// <summary>
///     These contributions flow through the incremental pipeline embedded in MutationModel. They must be
///     value-equatable (EquatableArray, not raw ImmutableArray) or the generator re-runs on every keystroke.
/// </summary>
public sealed class ContributionEqualityTests
{
    [Fact]
    public void PresetContribution_WithEqualProviders_AreEqual()
    {
        var a = new PresetContribution { Providers = BuildProviders() };
        var b = new PresetContribution { Providers = BuildProviders() };

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    private static ImmutableArray<PresetProviderModel> BuildProviders() =>
    [
        new PresetProviderModel { ProviderTypeFqn = "global::MyApp.ReservationPresetProvider", Order = 0 },
    ];
}
