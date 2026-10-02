using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Emit;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.SourceGenerator;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Drives the transition contracts through the real transform, not a hand-built model.
///     <para>
///         The Showcase declares <c>[TransitionsTo&lt;InvoiceStatus&gt;]</c> on an endpoint and no transition
///         file was produced at all: the entity is created by a <c>DomainAction</c>, which is deliberately
///         excluded from generated CRUD contracts, so no create could be correlated and the extractor returned
///         null. A declared state machine looked covered while nothing existed for it.
///     </para>
/// </summary>
public class StateTransitionGenerationTests
{
    private const string Preamble = """
        namespace Pragmatic.Endpoints.Attributes
        {
            public enum HttpVerb { Get, Post, Put, Patch, Delete }
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(HttpVerb method, string route) { }
            }
            public sealed class EndpointGroupAttribute : System.Attribute
            {
                public EndpointGroupAttribute(string routePrefix) { }
            }
            public sealed class EndpointGroupAttribute<TGroup> : System.Attribute where TGroup : class { }
        }
        namespace Pragmatic.Actions.Attributes
        {
            public sealed class TransitionsToAttribute<TState> : System.Attribute
            {
                public TransitionsToAttribute(TState target) { }
            }
        }
        namespace Pragmatic.Authorization
        {
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(string permission) { }
            }
        }
        namespace Pragmatic.Actions.Mutation { public abstract class Mutation<TEntity> { } }
        namespace Pragmatic.Actions.Action { public abstract class DomainAction<TResult> { } }
        namespace Pragmatic.Persistence.Repository { public interface IRepository<TEntity> { } }
        namespace Pragmatic.Persistence.StateMachine
        {
            public sealed class InitialStateAttribute : System.Attribute { }
            public sealed class TransitionFromAttribute : System.Attribute
            {
                public TransitionFromAttribute(object source) { }
            }
        }
        namespace App.Billing.Entities { public class Invoice { } }
        namespace App.Billing.Enums
        {
            public enum InvoiceStatus
            {
                [Pragmatic.Persistence.StateMachine.InitialState]
                Draft,
                [Pragmatic.Persistence.StateMachine.TransitionFrom(Draft)]
                Paid
            }
        }
        """;

    /// <summary>The transition endpoint, plus whatever create the scenario provides.</summary>
    private const string TransitionEndpoint = """
        namespace App.Billing.Endpoints
        {
            [Pragmatic.Endpoints.Attributes.EndpointGroup("/api/invoices")]
            public sealed class InvoicesGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/{id}/pay")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.update")]
            [Pragmatic.Actions.Attributes.TransitionsTo<App.Billing.Enums.InvoiceStatus>(App.Billing.Enums.InvoiceStatus.Paid)]
            public class MarkInvoicePaidEndpoint
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Billing.Entities.Invoice> _invoices;
            }
        }
        """;

    /// <summary>
    ///     The same transition, carrying the route's <c>{id}</c> as an ordinary member — bound by name,
    ///     with no <c>[FromRoute]</c>, which is how an action that already loads by id is written.
    /// </summary>
    private const string TransitionBindingTheRouteIdByConvention = """
        namespace App.Billing.Endpoints
        {
            [Pragmatic.Endpoints.Attributes.EndpointGroup("/api/invoices")]
            public sealed class InvoicesGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/{id}/pay")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.update")]
            [Pragmatic.Actions.Attributes.TransitionsTo<App.Billing.Enums.InvoiceStatus>(App.Billing.Enums.InvoiceStatus.Paid)]
            public class MarkInvoicePaidEndpoint
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Billing.Entities.Invoice> _invoices;
                public System.Guid Id { get; set; }
            }
        }
        """;

    /// <summary>The same transition, with a member that really does travel in the body.</summary>
    private const string TransitionTakingABody = """
        namespace App.Billing.Endpoints
        {
            [Pragmatic.Endpoints.Attributes.EndpointGroup("/api/invoices")]
            public sealed class InvoicesGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/{id}/pay")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.update")]
            [Pragmatic.Actions.Attributes.TransitionsTo<App.Billing.Enums.InvoiceStatus>(App.Billing.Enums.InvoiceStatus.Paid)]
            public class MarkInvoicePaidEndpoint
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Billing.Entities.Invoice> _invoices;
                public System.Guid Id { get; set; }
                public string Note { get; set; }
            }
        }
        """;

    /// <summary>
    ///     A create whose operation name is <b>not</b> <c>Create{Entity}</c>, so the three names the
    ///     contracts form for it are three different strings.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>CreateInvoiceEndpoint</c> cannot see this defect: its action name is <c>CreateInvoice</c>,
    ///     which is also what <c>"Create" + entity.Name</c> spells, so two of the three names collide by
    ///     accident and the test passes on a coincidence of the fixture.
    /// </remarks>
    private const string CreateNamedAfterItsVerb = """
        namespace App.Billing.Creates
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/invoices")]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.create")]
            public class RaiseInvoiceEndpoint : Pragmatic.Actions.Mutation.Mutation<App.Billing.Entities.Invoice>
            {
                public string Number { get; set; }
                public decimal Amount { get; set; }
            }
        }
        """;

    /// <summary>The entity that owns the state, alongside a projection carrying the same property.</summary>
    private const string StatefulEntity = """
        namespace App.Billing.Read
        {
            public class InvoiceSummaryDto { public App.Billing.Enums.InvoiceStatus Status { get; set; } }
        }
        namespace App.Billing.Model
        {
            public class Invoice { public App.Billing.Enums.InvoiceStatus Status { get; set; } }
        }
        """;

    /// <summary>A create the generator accepts: a Mutation with a fully synthesizable body.</summary>
    private const string SynthesizableCreate = """
        namespace App.Billing.Creates
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/invoices")]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.create")]
            public class CreateInvoiceEndpoint : Pragmatic.Actions.Mutation.Mutation<App.Billing.Entities.Invoice>
            {
                public string Number { get; set; }
                public decimal Amount { get; set; }
            }
        }
        """;

    /// <summary>
    ///     A create exposed as a <c>DomainAction</c> — the usual shape for an entity with a state machine,
    ///     which is created by a command rather than by a plain CRUD mutation. Carries an optional complex
    ///     field, which must be omitted from the synthesized body rather than sink the whole create.
    /// </summary>
    private const string DomainActionCreate = """
        namespace App.Billing.Commands
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/invoices")]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.create")]
            public class CreateInvoiceAction : Pragmatic.Actions.Action.DomainAction<System.Guid>
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Billing.Model.Invoice> _invoices;
                public string Number { get; set; }
                public App.Billing.Commands.ServiceFee[]? Fees { get; set; }
            }

            public class ServiceFee { public string Label { get; set; } }
        }
        """;

    /// <summary>A create the generator cannot use: a required foreign key it will not invent.</summary>
    private const string CreateWithForeignKey = """
        namespace App.Billing.Creates
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/invoices")]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.create")]
            public class CreateInvoiceEndpoint : Pragmatic.Actions.Mutation.Mutation<App.Billing.Entities.Invoice>
            {
                public string Number { get; set; }
                public System.Guid ReservationId { get; set; }
            }
        }
        """;

    private static MetadataReference[] BaseReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        ];
    }

    private static string Generate(string source, string fileNamePart)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("TransitionGenTest", [tree], BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ContractTestGenerator());
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(fileNamePart))?.GetText().ToString() ?? "";
    }

    /// <summary>
    ///     One create endpoint is asked for under exactly one name, whichever contract is asking.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It was asked for under <b>three</b>, measured on Invoicing's generated files: the auth
    ///     contract used the action's name (<c>CreateDraftInvoiceMutation</c>), the CRUD contract
    ///     prefixed it (<c>CreateCreateDraftInvoiceMutation</c>), and the transition contract used the
    ///     entity's (<c>CreateInvoice</c>). An application that supplies the body writes it once and it
    ///     answers for one of the three; the other two post the synthesised body, so the arrangement
    ///     fails and the contract reports nothing about what it exists to measure.
    ///     <para>
    ///         Only a test across the three files can see it — each template is right on its own, and the
    ///         disagreement lives between them.
    ///     </para>
    /// </remarks>
    [Fact]
    public void OneCreateEndpoint_IsAskedForUnderOneName()
    {
        var everything = string.Join("\n", GenerateAll(Preamble + TransitionEndpoint + CreateNamedAfterItsVerb));

        var namesForTheCreate = Regex.Matches(everything, "(?:Prepare|Body)\\([A-Za-z]*,? ?\"([A-Za-z_]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(name => name.Contains("Invoice", System.StringComparison.Ordinal)
                           && !name.Contains("_TransitionTo", System.StringComparison.Ordinal)
                           && name != "MarkInvoicePaid")
            .Distinct()
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();

        namesForTheCreate.Should().BeEquivalentTo(["RaiseInvoice"],
            "the create endpoint has one name — the operation's own, which is already what the auth "
            + "contract uses — so an application that supplies its body writes the key once");
    }

    /// <summary>Every contract file the generator produces for this compilation.</summary>
    private static string[] GenerateAll(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("TransitionGenTest", [tree], BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ContractTestGenerator());

        return [.. driver.RunGenerators(compilation).GetRunResult().GeneratedTrees
            .Select(t => t.GetText().ToString())];
    }

    [Fact]
    public void WithASynthesizableCreate_GeneratesTheTransitionFlow()
    {
        var source = Generate(Preamble + TransitionEndpoint + SynthesizableCreate, "Billing.Transitions");

        source.Should().Contain("class BillingTransitionContractTests");
        source.Should().Contain("Invoice_TransitionToPaid_FromInitial_Succeeds");
        source.Should().Contain("\"/api/invoices\"", "the entity is created first");
        source.Should().Contain("/pay", "then the transition route is posted");
        source.Should().NotContain("NotContractable", "a correlatable create means a real test, not a placeholder");
    }

    /// <summary>
    ///     A transition whose member is the route's own <c>{id}</c> posts no body.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Nothing marks it: an action that loads by id declares <c>public required Guid Id</c> and
    ///     lets the route bind it by name. Read as a body member it is a required <c>Guid</c> ending in
    ///     <c>Id</c> — a foreign key the synthesiser will not invent — so the whole body became
    ///     unsynthesisable and every such transition would have started demanding a <c>BodyFor</c> for a
    ///     request that takes no content at all. The route says which names are already spoken for.
    /// </remarks>
    [Fact]
    public void ATransitionWhoseOnlyMemberIsTheRouteId_PostsNoBody()
    {
        var source = Generate(
            Preamble + TransitionBindingTheRouteIdByConvention + SynthesizableCreate, "Billing.Transitions");

        source.Should().Contain("Invoice_TransitionToPaid_FromInitial_Succeeds", "the contract is emitted");
        source.Should().NotContain("transitionRequest.Content",
            "the id travels in the route, so there is no body to post");
        source.Should().NotContain("transitionBody",
            "and nothing is asked of the application for a request that takes no content");
    }

    /// <summary>
    ///     A member bound from a claim is filled by the invoker, not sent by the caller.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Read as a body member, <c>[FromClaim("sub")] Guid PaidByUserId</c> is a <c>Guid</c> ending in
    ///     <c>Id</c> — a foreign key the synthesiser will not invent — and the Showcase's mark-paid transition
    ///     demanded a <c>BodyFor</c> for a request that carries no content at all.
    /// </remarks>
    [Fact]
    public void ATransitionWithAClaimBoundMember_PostsNoBody()
    {
        var source = Generate(
            Preamble + TransitionBindingAClaim + SynthesizableCreate, "Billing.Transitions");

        source.Should().Contain("Invoice_TransitionToPaid_FromInitial_Succeeds", "the contract is emitted");
        source.Should().NotContain("transitionBody",
            "the claim is the invoker's to fill, so nothing is asked of the application");
    }

    private const string TransitionBindingAClaim = """
        namespace Pragmatic.Actions.Attributes
        {
            public sealed class FromClaimAttribute : System.Attribute
            {
                public FromClaimAttribute(string claimType) { }
            }
        }
        namespace App.Billing.Endpoints
        {
            [Pragmatic.Endpoints.Attributes.EndpointGroup("/api/invoices")]
            public sealed class InvoicesGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/{id}/pay")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.update")]
            [Pragmatic.Actions.Attributes.TransitionsTo<App.Billing.Enums.InvoiceStatus>(App.Billing.Enums.InvoiceStatus.Paid)]
            public class MarkInvoicePaidEndpoint
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Billing.Entities.Invoice> _invoices;
                public System.Guid Id { get; set; }

                [Pragmatic.Actions.Attributes.FromClaim("sub")]
                public System.Guid PaidByUserId { get; set; }
            }
        }
        """;

    /// <summary>The counterpart: a member that is not a route token is a body member, and is posted.</summary>
    [Fact]
    public void ATransitionWithAMemberOutsideTheRoute_PostsItAsTheBody()
    {
        var source = Generate(Preamble + TransitionTakingABody + SynthesizableCreate, "Billing.Transitions");

        source.Should().Contain("Note = \"test-\"", "the body is synthesised from the endpoint's own shape");
        source.Should().NotContain("Id = ", "the route parameter is not repeated in the body");
        source.Should().Contain(
            "transitionRequest.Content = global::System.Net.Http.Json.JsonContent.Create("
            + "global::Pragmatic.Testing.PragmaticContractHost.Body(\"Invoice_TransitionToPaid\", transitionBody));");
    }

    /// <summary>
    ///     An entity with no create route is arranged by the application, and the contract is a real test,
    ///     not a skipped placeholder, which a suite counts and nobody reads.
    /// </summary>
    [Fact]
    public void WithNoCorrelatableCreate_TheContractAsksTheApplicationToArrangeIt()
    {
        var source = Generate(Preamble + TransitionEndpoint, "Billing.Transitions");

        source.Should().NotBeEmpty("a declared transition must leave a trace, not vanish");
        source.Should().Contain("var id = await global::Pragmatic.Testing.PragmaticContractHost.ArrangeAsync(\"Invoice\", Client);");
        source.Should().NotContain("Skip = ");
    }

    /// <summary>
    ///     Mirrors the real layout: the Pragmatic attributes in their own assembly, the domain referencing it,
    ///     and the test project seeing the domain only as a <b>reference assembly</b> — which is what the SDK
    ///     produces and compiles against by default.
    ///     <para>
    ///         This is the case that was broken. The extractor identified the entity through the endpoint's
    ///         <c>IRepository&lt;TEntity&gt;</c> field, which is private, and reference assemblies strip
    ///         private members. Source-only tests passed while the Showcase — tests in their own project,
    ///         endpoints in referenced modules — produced no transition file at all.
    ///     </para>
    /// </summary>
    [Fact]
    public void WhenTheEndpointComesFromAReferenceAssembly_TheTransitionIsStillDiscovered()
    {
        var source = AcrossAssemblies(TransitionEndpoint + StatefulEntity);

        source.Should().NotBeEmpty(
            "a transition declared in a referenced module must still produce a contract or a stated reason");
        source.Should().Contain("Invoice_TransitionToPaid",
            "the entity must be identified without relying on a private field");
        source.Should().NotContain("InvoiceSummaryDto",
            "the entity must win over a projection carrying the same state property");
    }

    /// <summary>
    ///     Across assemblies, the command's entity is <c>Invoice</c> — not the command.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The name route exists because the repository field is private and reference assemblies strip
    ///     private members. It looked for a type whose name the command's name contains — and the command's
    ///     own name contains itself, so <c>CreateInvoiceAction</c> resolved to <c>CreateInvoiceAction</c>,
    ///     which no transition can ever correlate to. Invisible in a single compilation, where the private
    ///     field is still there and the name route is never reached.
    /// </remarks>
    [Fact]
    public void WhenTheCommandCreateComesFromAReferenceAssembly_TheEntityIsNotTheCommandItself()
    {
        var source = AcrossAssemblies(TransitionEndpoint + StatefulEntity + DomainActionCreate);

        source.Should().NotContain("NotContractable",
            "the command creates Invoice, and that is the entity the transition arranges");
        source.Should().Contain("Invoice_TransitionToPaid_FromInitial_Succeeds");
    }

    /// <summary>
    ///     Runs the generator the way the SDK lays a solution out: the attributes in their own assembly, the
    ///     domain referencing it, and the test project seeing the domain as a <b>reference assembly</b>.
    /// </summary>
    private static string AcrossAssemblies(string domainSource)
    {
        var framework = CSharpCompilation.Create("Pragmatic.Endpoints",
            [CSharpSyntaxTree.ParseText(Preamble, new CSharpParseOptions(LanguageVersion.Latest))],
            BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        framework.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var domain = CSharpCompilation.Create("App.Billing",
            [CSharpSyntaxTree.ParseText(domainSource, new CSharpParseOptions(LanguageVersion.Latest))],
            [.. BaseReferences(), framework.ToMetadataReference()],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        domain.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var consumer = CSharpCompilation.Create("App.Tests",
            [CSharpSyntaxTree.ParseText("// test project", new CSharpParseOptions(LanguageVersion.Latest))],
            [.. BaseReferences(), framework.ToMetadataReference(), AsReferenceAssembly(domain)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ContractTestGenerator());
        return driver.RunGenerators(consumer).GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Billing.Transitions"))?.GetText().ToString() ?? "";
    }

    /// <summary>Emits <paramref name="compilation"/> the way the SDK does: metadata only, private members stripped.</summary>
    private static MetadataReference AsReferenceAssembly(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream,
            options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
        result.Success.Should().BeTrue();
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    /// <summary>
    ///     A state machine's entity is normally created by a command, not by a CRUD mutation. Excluding
    ///     DomainActions from correlation left exactly those entities without any transition contract.
    /// </summary>
    [Fact]
    public void WithADomainActionCreate_GeneratesTheTransitionFlow()
    {
        var source = Generate(Preamble + TransitionEndpoint + StatefulEntity + DomainActionCreate,
            "Billing.Transitions");

        source.Should().Contain("Invoice_TransitionToPaid_FromInitial_Succeeds",
            "a command create can arrange the entity just as a mutation can");
        source.Should().NotContain("NotContractable");
        source.Should().Contain("Number = \"test-\"", "the synthesizable field is filled");
        source.Should().NotContain("Fees =", "an optional complex field is omitted, not invented");
    }

    /// <summary>A command create gets no CRUD round-trip contract of its own — only the transition uses it.</summary>
    [Fact]
    public void ADomainActionCreate_DoesNotGetItsOwnCrudContract()
    {
        var source = Generate(Preamble + TransitionEndpoint + StatefulEntity + DomainActionCreate,
            "Billing.Crud");

        source.Should().BeEmpty(
            "a DomainAction may have business preconditions a synthesized body does not meet");
    }

    /// <summary>
    ///     A create the shape cannot fill is still contracted: the application supplies the body.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not a skipped placeholder naming the foreign key: the synthesized body is not the only body
    ///     there can be — <c>PragmaticContractHost.BodyFor</c> lets the application supply what the shape
    ///     cannot carry — so refusing to emit would be refusing to ask.
    ///     <para>
    ///         What still skips is a transition with <b>no</b> create route at all: there is nothing for
    ///         an application to supply a body to.
    ///     </para>
    /// </remarks>
    [Fact]
    public void WithAnUnsynthesizableCreate_IsStillContracted_BecauseTheApplicationCanSupplyTheBody()
    {
        var source = Generate(Preamble + TransitionEndpoint + CreateWithForeignKey, "Billing.Transitions");

        source.Should().NotContain("Invoice_TransitionToPaid_NotContractable",
            "a create route exists, so the contract is emitted and the body is asked for");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticContractHost.Body(\"CreateInvoice\", body)");
    }
}
