using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[FromTenant]</c> on a query property (#741): the generated invoker fills it with the request's
///     tenant, and nothing else can — not the route, not the boundary, not a filter of its own.
/// </summary>
/// <remarks>
///     That the tenant read is the request's over HTTP, whatever the caller sends, is proved in the
///     Invoicing example (<c>TheTenantComesFromTheToken</c>); these pin the shape the generator decides.
/// </remarks>
public class AQueryReadsTheTenantTests
{
    private const string Model = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.MultiTenancy;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Specification;

        namespace TestApp
        {
            [Boundary]
            public partial class SalesBoundary { }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Company : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Slug { get; set; } = "";
                public string Name { get; set; } = "";
            }

            [PragmaticDbContext("Sales")]
            public partial class SalesDbContext { }
        }
        """;

    private static string Query(string members) => $$"""

        namespace TestApp
        {
            [Query<Company, Company>]
            [Endpoint(HttpVerb.Get, "api/companies/mine")]
            public partial class OwnCompanyQuery
            {
                {{members}}
            }
        }
        """;

    private const string Bound = """
        [FromTenant]
        public string Slug { get; private set; } = "";

        public Specification<Company> Mine => Spec<Company>.Where(c => c.Slug == Slug);

        [Filter]
        public string? Name { get; set; }
        """;

    [Fact]
    public void TheInvoker_WritesTheTenant_AndRefusesARequestWithNone()
    {
        var invoker = Invoker(Model + Query(Bound));

        invoker.Should().Contain("typeof(global::Pragmatic.MultiTenancy.ITenantContext)");
        invoker.Should().Contain("if (string.IsNullOrEmpty(__tenant))",
            "a string binding says the operation needs a tenant, and an empty one would read as one");
        invoker.Should().Contain("q.Slug = __tenant!;");
    }

    /// <summary>A <c>string?</c> binding takes no tenant as null, and refuses nothing.</summary>
    [Fact]
    public void ANullableBinding_TakesNoTenantAsNull()
    {
        var invoker = Invoker(Model + Query("""
            [FromTenant]
            public string? Slug { get; private set; }
            """));

        invoker.Should().Contain("q.Slug = __tenant;");
        invoker.Should().NotContain("IsNullOrEmpty(__tenant)");
    }

    /// <summary>The control: a query without the attribute reads no tenant.</summary>
    [Fact]
    public void AQueryWithoutIt_ReadsNoTenant()
        => Invoker(Model + Query("""
            [Filter]
            public string? Name { get; set; }
            """)).Should().NotContain("ITenantContext");

    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model + Query(Bound),
            static path => path.EndsWith(".QueryInvoker.g.cs") || path.EndsWith(".Query.g.cs")
                           || path.Contains("_Boundary.") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    [Fact]
    public void TheQuery_AppliesNoFilterForIt_AndTheRouteDoesNotReadIt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + Query(Bound));

        var apply = sources.Single(pair => pair.Key.EndsWith("OwnCompanyQuery.Query.g.cs")).Value;
        apply.Should().NotContain("e.Slug", "a specification reads it; the tenant filter does the rest");
        apply.Should().Contain("Mine");

        var endpoint = sources.Single(pair => pair.Key.EndsWith("OwnCompanyQuery.Endpoint.g.cs")).Value;
        endpoint.Should().NotContain("Slug", "a slug the caller sends is not read");
        endpoint.Should().Contain("Name", "the control: an ordinary input is still read");
    }

    [Fact]
    public void TheBoundary_DoesNotTakeIt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + Query(Bound));

        var facade = sources.Where(pair => pair.Key.Contains("_Boundary.SalesBoundary.")).ToList();

        facade.Should().Contain(pair => pair.Value.Contains("Name = name,"));
        foreach (var (hint, source) in facade)
            source.Should().NotContain("Slug", hint);
    }

    // =========================================================================
    // PRAG0747 — the binding cannot be written
    // =========================================================================

    [Theory]
    [InlineData("[FromTenant] public Guid Tenant { get; private set; }")]
    [InlineData("[FromTenant] public string Tenant { get; set; } = \"\";")]
    [InlineData("[FromTenant] public string Tenant { get; init; } = \"\";")]
    public void ABindingTheInvokerCannotWrite_IsPRAG0747(string declaration)
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model + Query(declaration));

        Ids(diagnostics).Should().Contain("PRAG0747");
        sources.Single(pair => pair.Key.EndsWith("OwnCompanyQuery.QueryInvoker.g.cs")).Value
            .Should().NotContain("ITenantContext", "what cannot be written is not generated");
    }

    [Fact]
    public void TheForm_IsNotReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Query(Bound));

        Ids(diagnostics).Should().NotContain("PRAG0747");
    }

    private static string Invoker(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        return sources.Single(pair => pair.Key.EndsWith("OwnCompanyQuery.QueryInvoker.g.cs")).Value;
    }

    private static IEnumerable<string> Ids(IEnumerable<Diagnostic> diagnostics) => diagnostics.Select(d => d.Id);
}
