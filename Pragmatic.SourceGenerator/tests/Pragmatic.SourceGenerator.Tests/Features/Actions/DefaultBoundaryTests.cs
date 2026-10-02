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
///     A module that declares no <c>[Boundary]</c> gets one named after itself, and everything that
///     hangs off a boundary is generated for it.
/// </summary>
/// <remarks>
///     The assertion that matters is not that the class appears — it is that the actions interface
///     appears with it. Emitting a class carrying <c>[Boundary]</c> and waiting for the boundary
///     pipeline to notice cannot work: a generator does not see its own output, so the type would sit
///     there with nothing attached. That is why the model is synthesised from <c>[Module]</c> instead.
/// </remarks>
public class DefaultBoundaryTests
{
    /// <summary>An entity and one mutation, so a boundary has something to expose.</summary>
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

    private static Dictionary<string, string> GeneratedFor(string body, bool asHost = false)
    {
        var source = $$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing;

            {{body}}
            """;

        var result = asHost
            ? GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
                source + "\n\npublic static class Program { public static void Main() { } }", References)
            : GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    }

    [Fact]
    public void AModuleWithoutABoundary_GetsOneNamedAfterIt()
    {
        var sources = GeneratedFor($$"""
            [Module]
            public sealed class BillingModule;

            {{Domain}}
            """);

        sources.Values.Should().Contain(text => text.Contains("partial class BillingBoundary"),
            "the module declares no boundary, so it gets one");
    }

    [Fact]
    public void TheGeneratedBoundary_CarriesTheInterfaceToo()
    {
        var sources = GeneratedFor($$"""
            [Module]
            public sealed class BillingModule;

            {{Domain}}
            """);

        sources.Values.Should().Contain(text => text.Contains("IBillingActions"),
            "a boundary nothing hangs off is the failure this exists to avoid: the interface has to be "
            + "generated from the same model, in the same pass");
    }

    /// <summary>The control: a declared boundary is used, and none is derived beside it.</summary>
    [Fact]
    public void ADeclaredBoundary_IsNotReplaced()
    {
        var sources = GeneratedFor($$"""
            [Module]
            public sealed class BillingModule;

            [Boundary]
            public partial class LedgerBoundary;

            {{Domain}}
            """);

        sources.Values.Should().NotContain(text => text.Contains("partial class BillingBoundary"),
            "the assembly declares a boundary of its own, so nothing is derived");
        sources.Values.Should().Contain(text => text.Contains("ILedgerActions"),
            "and the declared one is the one that gets the interface");
    }

    /// <summary>
    ///     The second control: a host declares <c>[Module]</c> too, and must not get a boundary — that
    ///     would invent an actions interface and a DbContext for the composition root.
    /// </summary>
    [Fact]
    public void AHostModule_GetsNoBoundary()
    {
        var sources = GeneratedFor("""
            [Module]
            public sealed class BillingModule;
            """, asHost: true);

        sources.Values.Should().NotContain(text => text.Contains("partial class BillingBoundary"),
            "the host is a composition root, not a boundary");
    }
}
