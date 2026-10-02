using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Incrementality regression for the entity aggregate providers. Adding an UNRELATED class must not
///     re-run the entity merge/fan-out (the set of entities is unchanged). Without value-equatable
///     comparers on the <c>ImmutableArray&lt;EntityMetadataModel&gt;</c> providers, every compilation change
///     re-generated every entity's artifacts.
/// </summary>
public class PersistenceIncrementalityTests
{
    private const string Source = """
        namespace Pragmatic.Persistence
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }
        namespace MyApp
        {
            [Pragmatic.Persistence.Entity]
            public partial class Product
            {
                public System.Guid Id { get; set; }
                public string Name { get; set; } = "";
            }
        }
        """;

    private const string UnrelatedAddition = """
        namespace MyApp
        {
            public class TotallyUnrelated
            {
                public int Value { get; set; }
            }
        }
        """;

    [Fact]
    public void AddingUnrelatedClass_DoesNotReRunEntityAggregateProviders()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(Source, UnrelatedAddition);

        var reexecuted = GeneratorTestHelper.GetReExecutedSteps(
            result,
            TrackingNames.PersistenceReferencedEntities,
            TrackingNames.PersistenceCurrentEntities,
            TrackingNames.PersistenceAllEntities);

        reexecuted.Should().BeEmpty(
            "the entity set is unchanged, so the value-equatable providers must stay cached");
    }
}
