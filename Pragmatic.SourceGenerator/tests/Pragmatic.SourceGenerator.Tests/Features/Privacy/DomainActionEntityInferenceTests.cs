using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The half of the domain-action story the template cannot test: whether the entities are actually
///     derived from the action's declared dependencies.
/// </summary>
/// <remarks>
///     <c>ProcessingOperationsTemplateTests</c> hands the template a list and checks what it renders. If
///     the transform found nothing, that suite stays green and the register silently omits every action
///     — the failure this repository keeps meeting. So these run the whole generator over real
///     declarations and read the emitted register.
/// </remarks>
public class DomainActionEntityInferenceTests
{
    private const string Subject = """
        namespace App
        {
            using Pragmatic.Privacy;

            [DataSubject("Email")]
            public class Customer
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                public string Email { get; set; } = "";
            }

            public class UpsertCustomerMutation { }
        }
        """;

    private static string? Register(string action)
    {
        var source = PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs
                     + PrivacyTestSources.OperationStubs + Subject + action;

        return GeneratorTestHelper
            .GetGeneratedSourcesAsDictionary(
                GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []))
            .Where(kv => kv.Key.Contains("_Infra.Privacy.ProcessingActivities"))
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    [Fact]
    public void AnActionHoldingARepository_IsListedAgainstThatEntity()
    {
        var register = Register("""

            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers/purge")]
                public partial class PurgeCustomersAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private Pragmatic.Persistence.Repository.IRepository<Customer> _customers = null!;
                }
            }
            """);

        register.Should().NotBeNull();
        register!.Should().Contain("\"App.PurgeCustomersAction\"").And.Contain("\"App.Customer\"");
    }

    /// <summary>
    ///     The shape reading repositories alone would have missed.
    /// </summary>
    /// <remarks>
    ///     An action that only composes owns no repository — <c>ImportMembersAction</c> in the consumer
    ///     application says so in a comment — and reaches the entity through an invoker instead. It is
    ///     also the shape most likely to process personal data in bulk, so missing it would have left the
    ///     register wrong exactly where it matters.
    /// </remarks>
    [Fact]
    public void AnActionThatOnlyComposes_IsListedThroughItsInvoker()
    {
        var register = Register("""

            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers/import")]
                public partial class ImportCustomersAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private Pragmatic.Actions.Invoker.IMutationInvoker<UpsertCustomerMutation, Customer> _upsert = null!;
                }
            }
            """);

        register.Should().NotBeNull();
        register!.Should().Contain("\"App.ImportCustomersAction\"").And.Contain("\"App.Customer\"");
    }

    [Fact]
    public void AnActionWithNoDerivableDependency_StaysOut()
    {
        // Not listed against a guess. The register's value is that every row in it is derived.
        var register = Register("""

            namespace App
            {
                public interface ISearchIndex { }

                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/index/rebuild")]
                public partial class RebuildIndexAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private ISearchIndex _index = null!;
                }
            }
            """);

        (register ?? string.Empty).Should().NotContain("RebuildIndexAction");
    }
}
