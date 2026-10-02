using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The Article 30 register lists what an operation loads — an <c>IReadRepository&lt;T&gt;</c>, a
///     <c>[LoadEntity]</c> / <c>[LoadEntities]</c>, a <c>[LoadFrom&lt;TQuery&gt;]</c> — without a
///     <c>[ProcessesData&lt;T&gt;]</c> restating it, and says so when one does.
/// </summary>
/// <remarks>
///     Reading <c>IRepository</c> and <c>IMutationInvoker</c> alone is not enough. A load's repository
///     field is generated, so no dependency the transform can read names its entity: without reading the
///     load declarations, an action that only preloads would be absent from the register unless its
///     author wrote the declaration by hand.
/// </remarks>
public class TheRegisterInfersWhatAnOperationLoadsTests
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

            [Pragmatic.Persistence.Query.Attributes.Query<Customer, Customer>]
            public partial class CustomersQuery { }
        }
        """;

    /// <summary>What the loads and the declaration look like; the real ones ship in their packages.</summary>
    private const string LoadStubs = """
        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class LoadEntityAttribute<TEntity> : System.Attribute
            {
                public LoadEntityAttribute() { }
                public LoadEntityAttribute(string idPropertyName) { }
                public string? Specification { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class LoadEntitiesAttribute<TEntity> : System.Attribute
            {
                public LoadEntitiesAttribute() { }
                public LoadEntitiesAttribute(string idsPropertyName) { }
                public string? Specification { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class LoadFromAttribute<TQuery> : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class RequireExistsAttribute<TEntity> : System.Attribute
            {
                public RequireExistsAttribute(string keyPropertyName) { }
            }
        }
        namespace Pragmatic.Persistence.Repository
        {
            public interface IReadRepository<TEntity> { }
        }
        namespace Pragmatic.Persistence.Query.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class QueryAttribute<TEntity, TResult> : System.Attribute { }
        }
        namespace Pragmatic.Actions.Mutation
        {
            public abstract class Mutation<TEntity> { }
        }
        namespace Pragmatic.Privacy
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class ProcessesDataAttribute<TEntity> : System.Attribute { }
        }
        """;

    private static (string Register, ImmutableArrayOfDiagnostics Diagnostics) Run(string operation)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs
            + PrivacyTestSources.OperationStubs + LoadStubs + Subject + operation,
            []);

        var register = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("_Infra.Privacy.ProcessingActivities"))
            .Select(kv => kv.Value)
            .FirstOrDefault() ?? string.Empty;

        return (register, new ImmutableArrayOfDiagnostics(GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG29").ToList()));
    }

    private static string Action(string attributes, string members = "") => $$"""

        namespace App
        {
            [Pragmatic.Actions.Attributes.DomainAction]
            {{attributes}}
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers/touch")]
            public partial class TouchCustomerAction : Pragmatic.Actions.Abstractions.VoidDomainAction
            {
                public System.Guid CustomerId { get; init; }
                {{members}}
            }
        }
        """;

    [Fact]
    public void AnActionThatLoadsAnEntity_IsListedAgainstIt()
    {
        var (register, _) = Run(Action("[Pragmatic.Actions.Attributes.LoadEntity<Customer>(nameof(CustomerId))]"));

        register.Should().Contain("\"App.TouchCustomerAction\"").And.Contain("\"App.Customer\"",
            "the load's repository field is generated, so only the attribute can name the entity");
    }

    [Fact]
    public void AnActionThatLoadsAListOrByARule_IsListedAgainstIt()
    {
        var (register, _) = Run(Action("[Pragmatic.Actions.Attributes.LoadEntities<Customer>(Specification = \"Active\")]"));

        register.Should().Contain("\"App.TouchCustomerAction\"").And.Contain("\"App.Customer\"");
    }

    /// <summary>
    ///     <c>[RequireExists&lt;T&gt;]</c> reads the row too — as an <c>EXISTS</c> — so the operation processes it,
    ///     And a <c>[ProcessesData&lt;T&gt;]</c> restating it is redundant.
    /// </summary>
    [Fact]
    public void AnActionThatRequiresARowToExist_IsListedAgainstIt_AndADeclarationBesideItIsRedundant()
    {
        var (register, _) = Run(Action("[Pragmatic.Actions.Attributes.RequireExists<Customer>(nameof(CustomerId))]"));
        register.Should().Contain("\"App.TouchCustomerAction\"").And.Contain("\"App.Customer\"");

        var (_, diagnostics) = Run(Action(
            "[Pragmatic.Actions.Attributes.RequireExists<Customer>(nameof(CustomerId))]\n[Pragmatic.Privacy.ProcessesData<Customer>]"));
        diagnostics.Items.Where(d => d.Id == "PRAG2913").Should().ContainSingle();
    }

    [Fact]
    public void AnActionReadingThroughAReadRepository_IsListedAgainstIt()
    {
        var (register, _) = Run(Action("",
            "private Pragmatic.Persistence.Repository.IReadRepository<Customer> _customers = null!;"));

        register.Should().Contain("\"App.TouchCustomerAction\"").And.Contain("\"App.Customer\"");
    }

    [Fact]
    public void AnActionReadingADeclaredQuery_IsListedAgainstTheQuerysEntity()
    {
        var (register, _) = Run(Action("",
            "[Pragmatic.Actions.Attributes.LoadFrom<CustomersQuery>] private System.Collections.Generic.IReadOnlyList<Customer> Found { get; set; } = [];"));

        register.Should().Contain("\"App.TouchCustomerAction\"").And.Contain("\"App.Customer\"");
    }

    /// <summary>A mutation reaches its own entity and what it preloads besides it.</summary>
    [Fact]
    public void AMutationsPreload_IsListedBesideItsOwnEntity()
    {
        var (register, _) = Run("""

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Reference")]
                public class Supplier
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Reference { get; set; } = "";
                }

                [Pragmatic.Actions.Attributes.LoadEntity<Customer>(nameof(CustomerId))]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Put, "api/suppliers/{id}")]
                public partial class RenameSupplierMutation : Pragmatic.Actions.Mutation.Mutation<Supplier>
                {
                    public System.Guid CustomerId { get; init; }
                }
            }
            """);

        var rows = register.Split("new global::Pragmatic.Privacy.ProcessingOperation(").Skip(1)
            .Where(r => r.Contains("\"App.RenameSupplierMutation\"")).ToList();
        rows.Should().Contain(r => r.Contains("\"App.Supplier\""));
        rows.Should().Contain(r => r.Contains("\"App.Customer\""), "the preloaded entity is processed by the mutation too");
    }

    /// <summary>
    ///     <c>[LoadCurrentUser]</c> reaches the <c>[PragmaticUser]</c> entity — which one is Identity's to say,
    ///     so it comes through the pipeline, and a declaration of it beside the load is redundant too.
    /// </summary>
    [Fact]
    public void AnActionThatLoadsTheSignedInUser_IsListedAgainstTheUserEntity()
    {
        var (register, diagnostics) = Run("""

            namespace Pragmatic.Identity
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class PragmaticUserAttribute : System.Attribute { }
            }
            namespace Pragmatic.Actions.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class LoadCurrentUserAttribute : System.Attribute { }
            }
            namespace App
            {
                using Pragmatic.Privacy;

                [Pragmatic.Identity.PragmaticUser]
                [DataSubject("Nickname")]
                public class Account
                {
                    public System.Guid Id { get; set; }

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Nickname { get; set; } = "";
                }

                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Actions.Attributes.LoadCurrentUser]
                [Pragmatic.Privacy.ProcessesData<Account>]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/me/touch")]
                public partial class TouchMeAction : Pragmatic.Actions.Abstractions.VoidDomainAction { }
            }
            """);

        register.Should().Contain("\"App.TouchMeAction\"").And.Contain("\"App.Account\"");
        diagnostics.Items.Where(d => d.Id == "PRAG2913").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("TouchMeAction") && m.Contains("Account"));
    }

    /// <summary>The control: an action reaching nothing the generator can see is still absent.</summary>
    [Fact]
    public void AnActionThatLoadsNothing_StaysOut()
    {
        var (register, _) = Run(Action(""));

        register.Should().NotContain("TouchCustomerAction");
    }

    [Fact]
    public void ADeclarationTheGeneratorInfers_IsReportedAsRedundant()
    {
        var (_, diagnostics) = Run(Action(
            "[Pragmatic.Actions.Attributes.LoadEntity<Customer>(nameof(CustomerId))]\n[Pragmatic.Privacy.ProcessesData<Customer>]"));

        diagnostics.Items.Where(d => d.Id == "PRAG2913").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("TouchCustomerAction") && m.Contains("Customer"));
        diagnostics.Items.Where(d => d.Id == "PRAG2913").Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Info);
    }

    /// <summary>The control: a declaration the generator cannot infer is the declaration's job, not redundant.</summary>
    [Fact]
    public void ADeclarationOnlyItCanMake_IsNotReported()
    {
        var (register, diagnostics) = Run(Action(
            "[Pragmatic.Privacy.ProcessesData<Customer>]", "private IAppInternalActions _app = null!;"));

        diagnostics.Items.Should().NotContain(d => d.Id == "PRAG2913");
        register.Should().Contain("\"App.Customer\"");
    }

    /// <summary>
    ///     The control on the other side: an operation that also composes through a boundary interface keeps
    ///     its declarations — one may be answering for the composition, which is what PRAG2911 asks for.
    /// </summary>
    [Fact]
    public void ADeclarationOnAComposingOperation_IsNotReported()
    {
        var (_, diagnostics) = Run(Action(
            "[Pragmatic.Actions.Attributes.LoadEntity<Customer>(nameof(CustomerId))]\n[Pragmatic.Privacy.ProcessesData<Customer>]",
            "private IAppInternalActions _app = null!;"));

        diagnostics.Items.Should().NotContain(d => d.Id == "PRAG2913");
        diagnostics.Items.Should().NotContain(d => d.Id == "PRAG2911", "the declaration still answers for the composition");
    }

    /// <summary>The diagnostics of one run, as a list the assertions can walk.</summary>
    private sealed record ImmutableArrayOfDiagnostics(System.Collections.Generic.List<Diagnostic> Items);
}
