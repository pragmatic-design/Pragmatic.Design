using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Xunit;
using EntitySuppressor = Pragmatic.SourceGenerator.Suppressors.EntityPropertyNullabilitySuppressor;

namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     PRAGS001 suppresses CS8618 because "entity properties are initialized by the SG-generated
///     Create() factory method and trait templates". Only properties the factory can actually write
///     — i.e. properties with a non-public setter on a partial [Entity] — are covered by that claim.
/// </summary>
public class EntityPropertyNullabilitySuppressorTests
{
    private static Task<System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> RunAsync(string source)
        => SuppressorTestHarness.RunAsync(
            new EntitySuppressor(),
            producer: null,
            (PragmaticAttributeStubs.Path, PragmaticAttributeStubs.Source),
            (SuppressorTestHarness.HandWrittenPath, source));

    // (1) Legitimate case — the SG-generated Create() factory writes this property.
    [Fact]
    public async Task PrivateSetterProperty_OnPartialEntity_IsSuppressed()
    {
        var diagnostics = await RunAsync("""
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Product
            {
                public string Name { get; private set; }
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "CS8618", "Name").IsSuppressed.Should().BeTrue();
    }

    // (2) Hand-written case — a public setter is the developer's own property; the factory does not
    // own it, so the CS8618 is real and must stay visible.
    [Fact]
    public async Task PublicSetterProperty_OnPartialEntity_IsNotSuppressed()
    {
        var diagnostics = await RunAsync("""
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Product
            {
                public string Note { get; set; }
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "CS8618", "Note").IsSuppressed.Should().BeFalse();
    }

    // (2b) Hand-written case — a field is never touched by the generated factory.
    [Fact]
    public async Task Field_OnPartialEntity_IsNotSuppressed()
    {
        var diagnostics = await RunAsync("""
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Product
            {
                public string Tag;
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "CS8618", "Tag").IsSuppressed.Should().BeFalse();
    }

    // (2c) Hand-written case — without `partial` there is no generated half at all.
    [Fact]
    public async Task PrivateSetterProperty_OnNonPartialEntity_IsNotSuppressed()
    {
        var diagnostics = await RunAsync("""
            [Pragmatic.Persistence.Entity.Entity]
            public class Product
            {
                public string Name { get; private set; }
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "CS8618", "Name").IsSuppressed.Should().BeFalse();
    }

    // Sanity: a non-Pragmatic type is untouched.
    [Fact]
    public async Task PrivateSetterProperty_OnPlainType_IsNotSuppressed()
    {
        var diagnostics = await RunAsync("""
            public partial class Product
            {
                public string Name { get; private set; }
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "CS8618", "Name").IsSuppressed.Should().BeFalse();
    }
}
