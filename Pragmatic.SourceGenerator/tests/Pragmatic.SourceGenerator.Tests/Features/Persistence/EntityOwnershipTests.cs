using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Which boundary owns an entity: the boundary claims it, the entity never names one.
/// </summary>
/// <remarks>
///     One boundary in the assembly owns everything, so nothing is declared in the common case. Two or
///     more and each says what is its own with <c>[Owns&lt;T&gt;]</c> — an entity nobody claims reaches
///     no DbContext, which is an absence rather than a failure, so it is an error rather than a warning.
/// </remarks>
public class EntityOwnershipTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Mutation<>)),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing;

            {{body}}

            public static class Program { public static void Main() { } }
            """, References);

    /// <summary>The generated DbContext of one boundary, or empty when none was emitted.</summary>
    private static string DbContextOf(SourceGenRunResult result, string boundary)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var hit = sources.FirstOrDefault(kv => kv.Key.Contains("DbContext." + boundary));
        return hit.Value ?? "";
    }

    [Fact]
    public void OneBoundary_OwnsEveryEntityWithoutSayingSo()
    {
        var result = Run("""
            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0629").Should().BeEmpty(
            "one boundary owns everything the assembly declares, so nothing has to be written");

        DbContextOf(result, "Billing").Should().Contain("DbSet<Contoso.Billing.Invoice>",
            "the entity reaches that boundary's context, which is what owning it means");
    }

    [Fact]
    public void TwoBoundaries_TheOneThatClaimsItOwnsIt()
    {
        var result = Run("""
            [Boundary]
            [Owns<Invoice>]
            public partial class BillingBoundary;

            [Boundary]
            public partial class LedgerBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0629").Should().BeEmpty(
            "BillingBoundary claims it");
        DbContextOf(result, "Billing").Should().Contain("DbSet<Contoso.Billing.Invoice>",
            "BillingBoundary claims it, so it is in BillingBoundary's context");
        DbContextOf(result, "Ledger").Should().NotContain("Invoice",
            "and not in the other one — the control that makes the first assertion mean something");
    }

    [Fact]
    public void TwoBoundaries_AnEntityNobodyClaims_IsPrag0629()
    {
        var result = Run("""
            [Boundary]
            public partial class BillingBoundary;

            [Boundary]
            public partial class LedgerBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0629").Should().BeTrue(
            "with a choice to make, an unclaimed entity reaches no DbContext and no migration creates "
            + "its table");
    }

    [Fact]
    public void TwoBoundaries_AnEntityBothClaim_IsPrag0630()
    {
        var result = Run("""
            [Boundary]
            [Owns<Invoice>]
            public partial class BillingBoundary;

            [Boundary]
            [Owns<Invoice>]
            public partial class LedgerBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0630").Should().BeTrue(
            "one entity belongs to one context, or the transaction line runs through the middle of it");
    }

    [Fact]
    public void TwoModulesInOneAssembly_IsPrag0628()
    {
        var result = Run("""
            [Module]
            public sealed class BillingModule;

            [Module]
            public sealed class LedgerModule;

            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0628").Should().BeTrue(
            "the host assigns a database to a module and finds the module by its assembly, so two "
            + "modules in one assembly are two answers to one question");
    }

    /// <summary>The control for the one above: a single module is silent.</summary>
    [Fact]
    public void OneModuleInOneAssembly_IsSilent()
    {
        var result = Run("""
            [Module]
            public sealed class BillingModule;

            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0628").Should().BeFalse();
    }
}
