using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     <c>[LinksToSubject]</c> follows a navigation declared with <c>[Relation]</c>.
/// </summary>
/// <remarks>
///     The path was looked up among the members declared in source, and a navigation the relation
///     declares is generated: not a symbol during this pass. So the form the framework recommends for
///     navigations was the one form a privacy path could not name — PRAG2906, and PRAG2900 behind it,
///     with no workaround, since the foreign key is generated too.
/// </remarks>
public class ALinkThroughADeclaredNavigationTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    /// <summary>The runtime contracts the adapters implement, stubbed without the persistence markers.</summary>
    private const string RuntimeStubs = """
        namespace Pragmatic.Privacy
        {
            public interface IPersonalDataSource { }
            public interface IErasureStep { }
            public interface IProcessingActivitySource { }
            public interface ISubjectRegistry { }
        }
        """;

    private const string Source = """

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

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [LinksToSubject("Customer")]
            public partial class Order : IEntity
            {
                [PersonalData(DataCategory.Location, Erasure = ErasureStrategy.Null)]
                public string ShippingAddress { get; set; } = "";
            }
        }
        """;

    private static SourceGenRunResult Run()
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + RuntimeStubs + Source, References);

    [Fact]
    public void ThePath_IsFollowed_NotReportedAsMissing()
    {
        var result = Run();

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2906").Should().BeFalse("the navigation exists once generated");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2900").Should().BeFalse("the order reaches its customer");
    }

    [Fact]
    public void TheLinkedEntitysSource_FiltersThroughTheNavigation()
    {
        var source = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run())
            .Where(kv => kv.Key.Contains("OrderPersonalDataSource"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        source.Should().NotBeNull();
        source!.Should().Contain("row => row.Customer.Email == identity");
    }

    /// <summary>
    ///     A navigation declared <c>Customer?</c> leads to the customer, like any other.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It used not to. The path was resolved to <c>ToDisplayString()</c>, which
    ///     carries the nullable annotation — so the target read <c>App.Customer?</c> and matched no
    ///     declared subject, and an optional link was reported as no link at all: PRAG2900, "this data
    ///     can never be erased", about an entity whose route was written correctly. Optional is the
    ///     ordinary shape of a navigation, which is why this one is worth pinning on its own.
    /// </remarks>
    [Fact]
    public void ANullableNavigation_LeadsToTheSubject_LikeAnyOther()
    {
        var source = PrivacyTestSources.Stubs + RuntimeStubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Email")]
                public class Customer
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; set; } = "";
                }

                [LinksToSubject("Customer")]
                public class Order
                {
                    [PersonalData(DataCategory.Location, Erasure = ErasureStrategy.Null)]
                    public string ShippingAddress { get; set; } = "";

                    public Customer? Customer { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2906").Should().BeFalse("the navigation exists");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2900").Should().BeFalse("the order reaches its customer");
    }
}
