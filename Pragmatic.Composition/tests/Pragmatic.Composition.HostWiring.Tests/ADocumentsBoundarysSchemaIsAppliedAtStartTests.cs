// Pragmatic.Composition.HostWiring.Tests - A documents boundary's schema is applied when the host starts

using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that includes a module persisted by the documents engine applies that boundary's schema (<c>{Boundary}DocSchema</c>, which the
///     module's assembly declares) at start, with the migrations, through the boundary's session. What the backend cannot apply stops the
///     start, as a failed migration does; so does a boundary whose session nothing registered, by name.
/// </summary>
/// <remarks>
///     The documents engine's bridge is referenced by these compilations only (<see cref="HostWiringFixture.EngineBridge" />), so every
///     other test here keeps EF Core as the only engine its modules reference. A stand-in is not enough: the module writes its schema only
///     with the bridge's unit of work.
/// </remarks>
public sealed class ADocumentsBoundarysSchemaIsAppliedAtStartTests
{
    private const string Module = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Reviews
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Documents)]
            public partial class ReviewsBoundary;

            [Module(Name = "Shop.Reviews")]
            public sealed class ReviewsModule;
        }

        namespace Shop.Reviews.Entities
        {
            [Entity]
            public partial class Review : IEntity
            {
                public string Text { get; private set; } = "";
            }
        }

        """;

    /// <summary>The documents engine's bridge, for these compilations only.</summary>
    private static readonly MetadataReference[] Bridge = [HostWiringFixture.EngineBridge("Pragmatic.Persistence.Documents")];

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Shop.Reviews;

        namespace Shop.Host;

        [Module]
        [Include<ReviewsModule>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void TheHostsStart_AppliesTheBoundarysDocumentsSchema_ThroughItsSession()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host, Bridge);

        errors.Should().BeEmpty("the host has to compile, or the schema is applied by text nobody runs");
        var start = generated.Values.SingleOrDefault(v => v.Contains("DocSchema.EnsureAsync", StringComparison.Ordinal));
        start.Should().NotBeNull("the boundary's assembly declares a documents schema, and the host applies it at start");
        start.Should().Contain("global::Shop.Reviews.ReviewsBoundaryDocSchema.EnsureAsync(");
        start.Should().Contain("GetKeyedService<global::Pragmatic.Persistence.Documents.DocSession>(documentsScope.ServiceProvider, typeof(global::Shop.Reviews.ReviewsBoundary))");
    }

    [Fact]
    public void TheHostsStart_StopsOnWhatTheBackendCannotApply_AndOnAMissingSession()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host, Bridge);

        errors.Should().BeEmpty();
        var start = generated.Values.Single(v => v.Contains("DocSchema.EnsureAsync", StringComparison.Ordinal));
        start.Should().Contain(".NotApplied.Count > 0");
        start.Should().Contain("[ReviewsBoundary] The documents schema could not be applied — host startup aborted");
        start.Should().Contain("call services.AddReviewsBoundaryPersistence(...)");
    }
}
