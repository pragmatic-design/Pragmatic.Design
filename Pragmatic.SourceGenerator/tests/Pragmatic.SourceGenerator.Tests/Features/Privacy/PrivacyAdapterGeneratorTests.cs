using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The layer that joins the generated privacy types to the runtime that consumes them: an
///     <c>IPersonalDataSource</c> and an <c>IErasureStep</c> per entity, the assembly's
///     <c>IProcessingActivitySource</c>, and the one registration that puts them into DI.
/// </summary>
/// <remarks>
///     Assertions are on the generated text. The stubs give the generator the type names it gates on,
///     not working implementations, so the resulting compilation cannot be expected to succeed —
///     <c>Pragmatic.Privacy.Tests</c> compiles the same generated code against the real packages and
///     runs it.
/// </remarks>
public class PrivacyAdapterGeneratorTests
{
    /// <summary>A subject and the entity that reaches it through one hop.</summary>
    private const string SubjectAndChild = """

        namespace App
        {
            using Pragmatic.Privacy;

            [DataSubject("Email")]
            public class Customer
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Retain,
                              Reason = "Art. 2220 c.c.")]
                public string Email { get; set; } = "";

                [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Null)]
                public string FullName { get; set; } = "";
            }

            [LinksToSubject("Customer")]
            public class Order
            {
                public Customer Customer { get; set; } = null!;

                [PersonalData(DataCategory.Location, Erasure = ErasureStrategy.Null)]
                public string ShippingAddress { get; set; } = "";
            }
        }
        """;

    private static string Source(string body)
        => PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs + body;

    private static IReadOnlyDictionary<string, string> Generated(string body)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(body), []));

    private static string? File(IReadOnlyDictionary<string, string> files, string hintFragment)
        => files.Where(kv => kv.Key.Contains(hintFragment)).Select(kv => kv.Value).FirstOrDefault();

    [Fact]
    public void ReachableEntities_EachGetASourceAndAnErasureStep()
    {
        var files = Generated(SubjectAndChild);

        File(files, "CustomerPersonalDataSource").Should()
            .NotBeNull("the subject's own rows are part of an access request");
        File(files, "OrderPersonalDataSource").Should()
            .NotBeNull("an entity that reaches the subject holds the subject's data too");
        File(files, "CustomerErasureStep").Should().NotBeNull();
        File(files, "OrderErasureStep").Should().NotBeNull();
    }

    /// <summary>
    ///     A soft-deleted row is still stored personal data: the erasure step reads past the
    ///     soft-delete filter, and past nothing else.
    /// </summary>
    /// <remarks>
    ///     Read through the raw set, the EF named filter <c>"SoftDelete"</c> hid a subject who had been
    ///     soft-deleted — and every row reached through a navigation to them. The erasure reported itself
    ///     total and erased none of it.
    /// </remarks>
    [Theory]
    [InlineData("CustomerErasureStep")]
    [InlineData("OrderErasureStep")]
    [InlineData("CustomerPersonalDataSource")]
    [InlineData("OrderPersonalDataSource")]
    public void TheStepsReachSoftDeletedRows_AndLiftNothingElse(string step)
    {
        var source = File(Generated(SubjectAndChild), step);

        source.Should().NotBeNull();
        source!.Should().Contain("IgnoreQueryFilters(new[] { \"SoftDelete\" })",
            "a soft-deleted subject is still a subject, and their rows are still their data");
        source.Should().NotContain("IgnoreQueryFilters()",
            "the tenant filter and every other named filter stay on");
    }

    [Fact]
    public void SourceForALinkedEntity_FiltersThroughTheDeclaredPath()
    {
        var source = File(Generated(SubjectAndChild), "OrderPersonalDataSource");

        source.Should().NotBeNull();
        source!.Should().Contain(
            "row => row.Customer.Email == identity",
            "an adapter that does not filter by the subject hands one person everybody's rows");
    }

    [Fact]
    public void ChainOfTwoHops_IsComposedRatherThanStoppingAtTheFirst()
    {
        // [LinksToSubject] carries one hop. OrderLine -> Order -> Customer is only a path once the two
        // are composed, and a source that stopped at Order would filter on nothing.
        var source = File(Generated(SubjectAndChild + """

            namespace App
            {
                using Pragmatic.Privacy;

                [LinksToSubject("Order")]
                public class OrderLine
                {
                    public Order Order { get; set; } = null!;

                    [PersonalData(DataCategory.Behavioural, Erasure = ErasureStrategy.Null)]
                    public string Note { get; set; } = "";
                }
            }
            """), "OrderLinePersonalDataSource");

        source.Should().NotBeNull();
        source!.Should().Contain("row => row.Order.Customer.Email == identity");
    }

    [Fact]
    public void ErasureOrder_PutsTheFurthestFromTheSubjectFirst()
    {
        var files = Generated(SubjectAndChild);

        File(files, "CustomerErasureStep")!.Should().Contain("public int Order => 100;");
        File(files, "OrderErasureStep")!.Should().Contain(
            "public int Order => 99;",
            "the child holds the foreign key, so it has to be cleared before the row it points at");
    }

    [Fact]
    public void EntityWithoutAPathToASubject_GetsNoAdapter()
    {
        // The one outcome worse than no adapter: an adapter with nothing to filter on, returning every
        // row in the table to whoever asked about one person.
        var files = Generated("""

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Email")]
                public class Customer
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; set; } = "";
                }

                public class Orphan
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; set; } = "";
                }
            }
            """);

        File(files, "OrphanPersonalDataSource").Should().BeNull();
        File(files, "OrphanErasureStep").Should().BeNull();
        File(files, "CustomerPersonalDataSource").Should().NotBeNull();
    }

    [Fact]
    public void BoundaryEntity_AsksForTheSameKeyedContextItsRepositoryDoes()
    {
        var source = File(Generated("""

            namespace App
            {
                using Pragmatic.Privacy;
                using Pragmatic.Persistence.Entity;

                public sealed class SalesBoundary { }

                [DataSubject("Email")]
                [BelongsTo<SalesBoundary>]
                public class Customer
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; set; } = "";
                }
            }
            """), "CustomerPersonalDataSource");

        source.Should().NotBeNull();
        source!.Should().Contain(
            "FromKeyedServices(typeof(global::App.SalesBoundary))",
            "an unkeyed DbContext would be a different database in any host with more than one boundary");
    }

    [Fact]
    public void GuidIdentifier_IsParsedBeforeTheQuery()
    {
        var source = File(Generated("""

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Retain,
                                  Reason = "primary key")]
                    public System.Guid Id { get; set; }
                }
            }
            """), "CustomerPersonalDataSource");

        source.Should().NotBeNull();
        source!.Should().Contain("global::System.Guid.TryParse(identity, out var subjectKey)")
            .And.Contain("row.Id == subjectKey");
    }

    [Fact]
    public void IdentifierOfATypeNothingCanLookUpBy_IsReportedAndProducesNoAdapter()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source("""

            namespace App
            {
                using Pragmatic.Privacy;

                public readonly record struct CustomerId(System.Guid Value);

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Retain,
                                  Reason = "primary key")]
                    public CustomerId Id { get; set; }
                }
            }
            """), []);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2908").Should()
            .NotBeEmpty("silently generating nothing would leave the subject invisible to both requests");

        File(GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result), "CustomerPersonalDataSource").Should()
            .BeNull();
    }

    [Fact]
    public void Registration_NamesEveryAdapterItGenerated()
    {
        var registration = File(Generated(SubjectAndChild), "_Infra.Privacy.Registration");

        registration.Should().NotBeNull(
            "the adapters are contributed, not discovered — nothing else puts them into DI");
        registration!.Should()
            .Contain("global::App.CustomerPersonalDataSource")
            .And.Contain("global::App.OrderPersonalDataSource")
            .And.Contain("global::App.CustomerErasureStep")
            .And.Contain("global::App.OrderErasureStep")
            .And.Contain("PragmaticProcessingActivitySource");
    }

    /// <summary>
    ///     The registration also brings in the services that consume what it contributes.
    /// </summary>
    /// <remarks>
    ///     Left to the application, forgetting it produced a container holding every adapter and nothing
    ///     that resolves them — and each of those services takes an <c>IEnumerable</c>, so the failure
    ///     mode was an access request returning nothing and reporting success.
    /// </remarks>
    [Fact]
    public void Registration_AlsoRegistersTheServicesThatConsumeTheAdapters()
        => File(Generated(SubjectAndChild), "_Infra.Privacy.Registration")!
            .Should().Contain("PrivacyServiceCollectionExtensions.AddPrivacy(services)");

    [Fact]
    public void Registration_UsesTryAddEnumerableSoASecondCallDoesNotDoubleTheSources()
    {
        // Two sources with the same Category is an exception at collect time, not a duplicate row.
        var registration = File(Generated(SubjectAndChild), "_Infra.Privacy.Registration");

        registration!.Should().Contain("TryAddEnumerable")
            .And.NotContain("services.AddScoped<global::Pragmatic.Privacy.IPersonalDataSource");
    }

    [Fact]
    public void ProcessingActivities_CarryTheCategoriesAndTheRetentionReason()
    {
        var activities = File(Generated(SubjectAndChild), "_Infra.Privacy.ProcessingActivities");

        activities.Should().NotBeNull("the Article 30 register is the point of classifying at all");
        activities!.Should()
            .Contain("\"App.Customer\"")
            .And.Contain("\"Contact\"")
            .And.Contain("Art. 2220 c.c.");
    }

    /// <summary>
    ///     A step whose entity is erased by destroying a key tells the orchestrator so.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The plan deliberately does <b>not</b> clear a <c>DestroyKey</c> field — clearing it is not
    ///     how it is erased — so an erasure with no key destroyer leaves it neither cleared nor
    ///     unreadable. The step reporting it is what lets <c>ErasureOrchestrator</c> refuse.
    ///     It is read off the plan rather than recomputed, so the two cannot disagree.
    /// </remarks>
    [Fact]
    public void AStepWhoseEntityIsCryptoShredded_TellsTheOrchestratorAKeyHasToGo()
    {
        var files = Generated("""

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Patient
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Special, Erasure = ErasureStrategy.DestroyKey, Encrypted = true)]
                    public string Diagnosis { get; set; } = "";
                }
            }
            """);

        var step = File(files, "PrivacyErasureStep");

        step.Should().NotBeNull();
        step!.Should().Contain(
            "RequiresKeyDestruction => global::App.PatientErasurePlan.RequiresKeyDestruction");
    }

    /// <summary>
    ///     The control: an entity nothing encrypts reports the same way, and answers no.
    /// </summary>
    /// <remarks>
    ///     Without it, "the step reports it" would be satisfied by a step that always says yes — and
    ///     the orchestrator would then refuse every erasure in every application that clears columns.
    /// </remarks>
    [Fact]
    public void AStepWhoseEntityEncryptsNothing_NeedsNoKey()
    {
        var files = Generated(SubjectAndChild);

        var plan = File(files, "PrivacyErase");

        plan.Should().NotBeNull();
        plan!.Should().Contain("RequiresKeyDestruction = false");
    }

    [Fact]
    public void Metadata_TellsTheHostWhichRegistrationToCall()
    {
        var metadata = File(Generated(SubjectAndChild), "_Metadata.PersonalData");

        metadata.Should().NotBeNull();
        metadata!.Should().Contain(
            "TestAssembly.Generated.PragmaticPrivacyRegistration.AddGeneratedPrivacyAdapters",
            "a payload describing personal data without naming who acts on it is what left the " +
            "generated types unreferenced in the first place");
    }

    [Fact]
    public void WithoutTheRuntimePackage_NothingIsGeneratedAgainstItsInterfaces()
    {
        // A module may reference only the zero-dependency attributes package. Emitting an adapter
        // against interfaces it cannot see would turn classifying a field into a build error.
        var files = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
                PrivacyTestSources.Stubs + SubjectAndChild, []));

        File(files, "PrivacySource").Should().BeNull();
        File(files, "PrivacyErasureStep").Should().BeNull();
        File(files, "_Infra.Privacy.Registration").Should().BeNull();
        File(files, "CustomerPersonalDataExtractor").Should()
            .NotBeNull("the classification-only outputs do not depend on the runtime");
    }
}
