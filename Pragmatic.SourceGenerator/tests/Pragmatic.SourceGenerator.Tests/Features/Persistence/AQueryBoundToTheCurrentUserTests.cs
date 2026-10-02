using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[FromCurrentUser]</c> on a query property: the generated invoker fills it from the caller, and
///     nothing else can.
/// </summary>
/// <remarks>
///     <para>
///         "My orders" is a read whose filter is who is asking. Without this the only way to take that
///         value was a public input the caller sends — so a caller could ask for someone else's — and
///         the reads ended up as actions that looked the caller up by hand.
///     </para>
///     <para>
///         The user entity declares no <c>Id</c>, as a Pragmatic entity never does: the member form names
///         a member this same generator writes, so <c>nameof(Customer.Id)</c> does not bind while the
///         transform runs. That is the shape every application has, so it is the one tested.
///     </para>
/// </remarks>
public class AQueryBoundToTheCurrentUserTests
{
    /// <summary>What Identity.Persistence would bring: the marker that turns the user resolver on.</summary>
    private const string Model = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Identity;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Pragmatic.Identity.Persistence.Entities
        {
            public class RolePermission { }
        }

        namespace TestApp
        {
            [Boundary]
            public partial class SalesBoundary { }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string OwnerId { get; set; } = "";
                public Guid BuyerId { get; set; }
            }

            [PragmaticDbContext("Sales")]
            public partial class SalesDbContext { }
        }
        """;

    private const string UserEntity = """

        namespace TestApp
        {
            [Entity]
            [BelongsTo<SalesBoundary>]
            [PragmaticUser]
            public partial class Customer : IEntity
            {
                public string ExternalIdentityKey { get; set; } = "";
                public string Email { get; set; } = "";
            }
        }
        """;

    private static string Query(string property, string route = "api/my-orders") => $$"""

        namespace TestApp
        {
            [Query<Order, Order>]
            [Endpoint(HttpVerb.Get, "{{route}}")]
            public partial class MyOrdersQuery
            {
                {{property}}
            }
        }
        """;

    private const string ById = """
        [FromCurrentUser]
        [Filter]
        public string OwnerId { get; private set; } = "";
        """;

    private const string ByMember = """
        [FromCurrentUser(nameof(Customer.Id))]
        [Filter]
        public Guid BuyerId { get; private set; }
        """;

    // =========================================================================
    // The invoker binds
    // =========================================================================

    /// <summary>Without a member, the property is the caller's id.</summary>
    [Fact]
    public void TheMemberlessForm_BindsTheCallersId()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ById));

        var invoker = Invoker(sources);

        invoker.Should().Contain("q.OwnerId = __currentUser.Id;");
        invoker.Should().Contain("UnauthorizedError",
            "a caller who is not authenticated has no id to bind, and is told so");
    }

    /// <summary>With a member, the property is that member of the user entity, read by its resolver.</summary>
    [Fact]
    public void TheMemberForm_BindsTheMember_ThroughTheResolver()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ByMember));

        var invoker = Invoker(sources);

        invoker.Should().Contain("new global::TestApp.CustomerResolver(",
            "the invoker builds the resolver it needs: nothing depends on it being registered");
        invoker.Should().Contain("q.BuyerId = __user.Id;");
        invoker.Should().Contain("NotFoundError",
            "an authenticated caller with no user entity has nothing to bind");
    }

    /// <summary>The binding happens inside the read — after validation and permission, before the executor.</summary>
    [Fact]
    public void TheBinding_ComesBeforeTheRead()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ByMember));

        var invoker = Invoker(sources);

        invoker.IndexOf("q.BuyerId = __user.Id;", System.StringComparison.Ordinal)
            .Should().BeLessThan(invoker.IndexOf("IQueryExecutor", System.StringComparison.Ordinal));
        invoker.IndexOf("RunAsync(query, async q =>", System.StringComparison.Ordinal)
            .Should().BeLessThan(invoker.IndexOf("q.BuyerId = __user.Id;", System.StringComparison.Ordinal),
                "inside the read the base runs after validation and the permission check");
    }

    /// <summary>Both forms compile, with the private setter the invoker is the only one to reach.</summary>
    [Fact]
    public void BothForms_Compile()
    {
        var (invokers, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model + UserEntity + Query(ById + ByMember),
            static path => path.EndsWith(".QueryInvoker.g.cs") || path.EndsWith(".Resolver.g.cs")
                           || path.Contains("_Boundary.") || path.EndsWith("TestSource.cs"));

        invokers.Should().BeEmpty(TraitCompilationHarness.FormatErrors(invokers));
    }

    /// <summary>
    ///     A string bound from the caller is matched exactly: <c>[Filter]</c> on a string otherwise means
    ///     <c>Contains</c>, and a caller whose id is contained in another's would read their rows.
    /// </summary>
    [Fact]
    public void AStringBoundFromTheCaller_IsMatchedExactly()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ById));

        var apply = sources.Single(pair => pair.Key.EndsWith("MyOrdersQuery.Query.g.cs")).Value;

        apply.Should().Contain("e.OwnerId == this.OwnerId");
        apply.Should().NotContain("OwnerId.Contains(");
    }

    /// <summary>
    ///     The bound value is always applied, <c>[Filter]</c> or not. An optional filter is skipped at its
    ///     default, and the one path that reaches <c>Apply</c> without the invoker would then read every
    ///     row instead of none.
    /// </summary>
    [Fact]
    public void ABoundProperty_IsAlwaysApplied()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query("""
            [FromCurrentUser]
            public string OwnerId { get; private set; } = "";
            """));

        var apply = sources.Single(pair => pair.Key.EndsWith("MyOrdersQuery.Query.g.cs")).Value;

        apply.Should().Contain("query = query.Where(e => e.OwnerId == this.OwnerId);");
        apply.Should().NotContain("this.OwnerId != default");
        Ids(diagnostics).Should().NotContain("PRAG0707", "the binding is what the value is for");
    }

    /// <summary>The control: an ordinary string filter still means <c>Contains</c>.</summary>
    [Fact]
    public void AnOrdinaryStringFilter_StillMeansContains()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query("""
            [Filter]
            public string? OwnerId { get; set; }
            """));

        var apply = sources.Single(pair => pair.Key.EndsWith("MyOrdersQuery.Query.g.cs")).Value;

        apply.Should().Contain("OwnerId.Contains(");
    }

    // =========================================================================
    // It is not a parameter
    // =========================================================================

    /// <summary>The generated route does not read the property from the request.</summary>
    [Fact]
    public void TheRoute_DoesNotReadTheProperty()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ById + ByMember));

        var endpoint = sources.Single(pair => pair.Key.EndsWith("MyOrdersQuery.Endpoint.g.cs")).Value;

        endpoint.Should().NotContain("OwnerId");
        endpoint.Should().NotContain("BuyerId");
    }

    /// <summary>Nor from the route: a placeholder that names it binds nothing and is reported.</summary>
    [Fact]
    public void ARoutePlaceholder_DoesNotBindIt()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(
            Model + UserEntity + Query(ByMember, route: "api/buyers/{buyerId}/orders"));

        var endpoint = sources.Single(pair => pair.Key.EndsWith("MyOrdersQuery.Endpoint.g.cs")).Value;

        endpoint.Should().NotContain("BuyerId =");
        diagnostics.Select(d => d.Id).Should().Contain("PRAG0504");
    }

    /// <summary>
    ///     Nor from another module: the boundary's overload that builds the query from its inputs does
    ///     not take the property, locally or remotely.
    /// </summary>
    [Fact]
    public void TheBoundary_DoesNotTakeTheProperty()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ById + ByMember + """
            [Filter]
            public string? Reference { get; set; }
            """));

        var facade = sources.Where(pair => pair.Key.Contains("_Boundary.SalesBoundary.")).ToList();

        facade.Should().NotBeEmpty();
        facade.Should().Contain(pair => pair.Value.Contains("Reference = reference,"),
            "the control: an ordinary input is still a parameter of the overload");
        foreach (var (hint, source) in facade)
        {
            source.Should().NotContain("OwnerId", hint);
            source.Should().NotContain("BuyerId", hint);
        }
    }

    // =========================================================================
    // PRAG0730 — the caller could set it
    // =========================================================================

    [Theory]
    [InlineData("public string OwnerId { get; set; } = \"\";")]
    [InlineData("public string OwnerId { get; init; } = \"\";")]
    [InlineData("public string OwnerId { get; internal set; } = \"\";")]
    public void ASetterTheCallerReaches_IsPRAG0730(string declaration)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query($"""
            [FromCurrentUser]
            [Filter]
            {declaration}
            """));

        Ids(diagnostics).Should().Contain("PRAG0730");
    }

    /// <summary>The control: a private setter is the form, and it is not reported.</summary>
    [Fact]
    public void APrivateSetter_IsNotReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query(ById + ByMember));

        Ids(diagnostics).Should().NotContain("PRAG0730");
        Ids(diagnostics).Should().NotContain("PRAG0731");
    }

    // =========================================================================
    // PRAG0731 — the binding cannot be generated
    // =========================================================================

    [Fact]
    public void AMemberTheUserEntityDoesNotHave_IsPRAG0731()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query("""
            [FromCurrentUser("Nickname")]
            [Filter]
            public string Nickname { get; private set; } = "";
            """));

        Ids(diagnostics).Should().Contain("PRAG0731");
        Invoker(sources).Should().NotContain("Nickname =",
            "a binding that cannot compile is not written into a file the author cannot edit");
    }

    [Fact]
    public void AMemberOfAnotherType_IsPRAG0731()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query("""
            [FromCurrentUser(nameof(Customer.Email))]
            [Filter]
            public Guid BuyerId { get; private set; }
            """));

        Ids(diagnostics).Should().Contain("PRAG0731");
    }

    /// <summary>
    ///     <c>nameof(Order.Id)</c> names a member the user entity also has. Read by its name alone it would
    ///     bind the user's id where the author wrote the order's, and compile.
    /// </summary>
    [Fact]
    public void AMemberOfAnotherEntity_IsPRAG0731()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query("""
            [FromCurrentUser(nameof(Order.Id))]
            [Filter]
            public Guid BuyerId { get; private set; }
            """));

        Ids(diagnostics).Should().Contain("PRAG0731");
    }

    [Fact]
    public void TheMemberFormWithNoUserEntity_IsPRAG0731()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Query("""
            [FromCurrentUser("Id")]
            [Filter]
            public Guid BuyerId { get; private set; }
            """));

        Ids(diagnostics).Should().Contain("PRAG0731");
    }

    [Fact]
    public void TheMemberlessFormOnANonString_IsPRAG0731()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + UserEntity + Query("""
            [FromCurrentUser]
            [Filter]
            public Guid OwnerId { get; private set; }
            """));

        Ids(diagnostics).Should().Contain("PRAG0731");
    }

    /// <summary>The control for the last one: the member-less form needs no user entity at all.</summary>
    [Fact]
    public void TheMemberlessForm_NeedsNoUserEntity()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Query(ById));

        Ids(diagnostics).Should().NotContain("PRAG0731");
    }

    private static string Invoker(Dictionary<string, string> sources)
        => sources.Single(pair => pair.Key.EndsWith("MyOrdersQuery.QueryInvoker.g.cs")).Value;

    private static IEnumerable<string> Ids(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(d => d.Id);
}
