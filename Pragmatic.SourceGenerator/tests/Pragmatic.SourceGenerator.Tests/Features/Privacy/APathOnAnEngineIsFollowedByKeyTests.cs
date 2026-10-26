using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     On the Sql or documents engine the privacy adapters follow the path to the subject by key, one read per hop: a document keeps a relation
///     as its key and has no navigation to follow. A hop whose key the generator cannot read is PRAG2914, and the entity gets no adapter.
///     The rows found and erased are shown by <c>PrivacyAdaptersTests</c> in the engine suites; this is what no suite can show.
/// </summary>
public class APathOnAnEngineIsFollowedByKeyTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
    ];

    /// <summary>The runtime contracts the adapters implement, and the Sql engine's bridge by the type the generator recognises it by.</summary>
    private const string RuntimeStubs = """
        namespace Pragmatic.Privacy
        {
            public interface IPersonalDataSource { }
            public interface IErasureStep { }
            public interface IProcessingActivitySource { }
            public interface ISubjectRegistry { }
        }
        namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }
        """;

    private const string Customer = """

        namespace App
        {
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Privacy;

            [Entity]
            [DataSubject("Email")]
            public partial class Customer : IEntity
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                public string Email { get; set; } = "";
            }
        }
        """;

    private const string DeclaredOrder = """

        namespace App
        {
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Privacy;

            [Entity]
            [Relation.ManyToOne<Customer>]
            [LinksToSubject("Customer")]
            public partial class Order : IEntity
            {
                [PersonalData(DataCategory.Location, Erasure = ErasureStrategy.Null)]
                public string? ShippingAddress { get; set; }
            }
        }
        """;

    private const string HandWrittenOrder = """

        namespace App
        {
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Privacy;

            [Entity]
            [LinksToSubject("Customer")]
            public partial class Order : IEntity
            {
                public Customer? Customer { get; set; }

                [PersonalData(DataCategory.Location, Erasure = ErasureStrategy.Null)]
                public string? ShippingAddress { get; set; }
            }
        }
        """;

    private static SourceGenRunResult Run(string order)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(PrivacyTestSources.Stubs + RuntimeStubs + Customer + order, References);

    [Fact]
    public void ADeclaredRelation_IsFollowedByItsKey()
    {
        var result = Run(DeclaredOrder);
        var source = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Single(s => s.Key.Contains("OrderErasureStep")).Value;

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2914").Should().BeFalse();
        source.Should().Contain("keys1.Contains(row.CustomerId)");
        source.Should().NotContain("DbContext");
    }

    [Fact]
    public void AHandWrittenNavigation_IsReportedAndGetsNoAdapter()
    {
        var result = Run(HandWrittenOrder);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2914").Should().BeTrue();
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.Should().NotContain(k => k.Contains("OrderErasureStep"));
    }
}
