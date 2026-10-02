using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Composition.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An operation can declare a field typed as the boundary interface of its own module, and the
///     generated invoker receives it.
/// </summary>
/// <remarks>
///     <para>
///         The interface does not exist when the field is classified: the same generator emits it later
///         in the same compilation, and a generator never sees its own output. So the symbol arrives as
///         an error type, which carries no signal at all — not an interface, no attributes, no base
///         chain. Left there it falls to <c>Ambiguous</c>, and the field is reported as unclassifiable
///         and <b>silently not injected</b>: PRAG0419 is a warning, the build succeeds, and the
///         operation throws on its first call.
///     </para>
///     <para>
///         The way out is to decide at compile time — what a generator needs and another part owns
///         travels through the pipeline rather than through a lookup. Here the other part is the same
///         generator: it knows which boundary interfaces it is about to emit, because it emits them,
///         so an unresolved name that is one of them is a service.
///     </para>
///     <para>
///         ⚠️ The control below is the half that keeps this honest. Widening the rule to "any
///         unresolved type is a service" would also make this test pass, and would turn every typo into
///         an injected field. A name that no boundary of this compilation could produce has to stay
///         ambiguous.
///     </para>
/// </remarks>
public class TheOwnBoundaryInterfaceIsInjectableTests
{
    /// <summary>An entity and a mutation, so the boundary has something to expose.</summary>
    private const string Domain = """
        [Entity]
        public partial class Invoice : IEntity
        {
            public string Number { get; private set; } = "";
        }

        [Mutation(Mode = MutationMode.Create)]
        [Endpoint(HttpVerb.Post, "api/invoices")]
        public partial class CreateInvoiceMutation : Mutation<Invoice>
        {
            public required string Number { get; init; }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<EndpointAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Mutation<>))
    ];

    private static SourceGenRunResult Run(string body)
    {
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing;

            [Module]
            public sealed class BillingModule;

            {{Domain}}

            {{body}}
            """;

        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);
    }

    private const string ActionWith = """
        [DomainAction]
        public partial class ReissueInvoiceAction : VoidDomainAction
        {
            private {{TYPE}} _billing = null!;
        }
        """;

    [Fact]
    public void TheInternalBoundaryInterface_IsInjected_AndNotReportedAmbiguous()
    {
        var result = Run(ActionWith.Replace("{{TYPE}}", "IBillingInternalActions"));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeFalse(
            "the interface is emitted by this same compilation, so an unresolved name that names one "
            + "is a service rather than unclassifiable state");

        var invoker = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(f => f.Key.Contains("ReissueInvoiceAction.Invoker")).Value;

        invoker.Should().Contain("IBillingInternalActions",
            "the diagnostic going quiet is not the claim: the constructor has to actually receive it, "
            + "and a field reported as injected but absent from the invoker is the same silent null "
            + "with one fewer warning");
    }

    /// <summary>The public facade of the same boundary, which an operation may also inject.</summary>
    [Fact]
    public void ThePublicBoundaryInterface_IsInjectedToo()
    {
        var result = Run(ActionWith.Replace("{{TYPE}}", "IBillingActions"));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeFalse();

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(f => f.Key.Contains("ReissueInvoiceAction.Invoker")).Value
            .Should().Contain("IBillingActions");
    }

    /// <summary>
    ///     The control: an unresolved name that no boundary here could produce stays ambiguous.
    /// </summary>
    /// <remarks>
    ///     Without this, "recognise the boundary interface" and "trust every name that does not
    ///     resolve" are the same green — and the second would inject a typo instead of reporting it.
    /// </remarks>
    [Fact]
    public void AnUnresolvedNameThatIsNotABoundaryInterface_IsStillReportedAmbiguous()
    {
        var result = Run(ActionWith.Replace("{{TYPE}}", "IPricingOracle"));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeTrue(
            "nothing in this compilation emits that name, so the generator cannot know what it is");
    }
}
