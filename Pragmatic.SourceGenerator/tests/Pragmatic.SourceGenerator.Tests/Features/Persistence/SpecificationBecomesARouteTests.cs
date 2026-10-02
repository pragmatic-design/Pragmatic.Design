using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A query derived from a specification gets the route written beside the specification.
/// </summary>
/// <remarks>
///     <para>
///         The derivation half — the type, its <c>Apply</c> and its <c>ToSpecification</c> — was landed
///         first: a rule written once became executable in-process through <c>IQueryExecutor</c>. What it
///         could not do is answer over HTTP, so <c>[Endpoint]</c> and <c>[RequirePermission]</c> written
///         on the specification reached nobody.
///     </para>
///     <para>
///         ⚠️ They could not even be written: both attributes were <c>AttributeTargets.Class</c>, so the
///         declaration was <c>CS0592</c> on the author's own line. A generator does not stop for an
///         attribute-usage error, which is why one case here asserts the author's file compiles at all —
///         asserting only on the generated output would measure nothing.
///     </para>
///     <para>
///         Runs through <see cref="TraitCompilationHarness" /> rather than a module test base: the route
///         crosses two features — the derivation is Persistence's, the handler is Endpoints' — and this
///         is the only harness that references both. Its name says trait because that is what it was
///         written for; what it does is run the whole generator with the whole reference closure.
///     </para>
/// </remarks>
public class SpecificationBecomesARouteTests
{
    private const string Common = """
        using System;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Specification;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Guid Id { get; set; }
            public Guid PersistenceId { get => Id; set => Id = value; }
            public string Code { get; set; } = "";
            public bool IsConfirmed { get; set; }
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }
        """;

    /// <summary>The specification carries the route, and the route is generated.</summary>
    [Fact]
    public void ASpecificationCarryingARoute_GetsOne()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            public static class OrderSpecs
            {
                [Query<Order>]
                [Endpoint(HttpVerb.Get, "api/orders/confirmed")]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        var generated = Endpoint(sources, "ConfirmedQuery");

        generated.Should().NotBeNull("the derived query needs a handler to be reachable");
        generated!.Should().Contain("MapGet(\"/api/orders/confirmed\"");
        generated.Should().Contain("ConfirmedQuery",
            "the route executes the derived query, not something else");
    }

    /// <summary>And the declaration is legal where the author writes it.</summary>
    /// <remarks>
    ///     The generated file above would appear even if the author's own file did not compile: a
    ///     generator does not stop for <c>CS0592</c>.
    /// </remarks>
    [Fact]
    public void TheDeclarationIsLegalOnTheSpecification()
    {
        var (_, other) = TraitCompilationHarness.CompileAndSplitErrors(Common + """

            public static class OrderSpecs
            {
                [Query<Order>]
                [Endpoint(HttpVerb.Get, "api/orders/confirmed")]
                [RequirePermission("sales.order.read")]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """, static _ => false);

        other.Where(d => d.Id == "CS0592")
            .Should().BeEmpty("[Endpoint] and [RequirePermission] belong on a specification");
    }

    /// <summary>The permission written beside the specification reaches the route.</summary>
    /// <remarks>
    ///     A route published without the permission its author declared is the failure this repository
    ///     keeps finding: the declaration is there, and the endpoint is as open as one carrying nothing.
    /// </remarks>
    [Fact]
    public void ThePermissionBesideTheSpecification_ReachesTheRoute()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            public static class OrderSpecs
            {
                [Query<Order>]
                [Endpoint(HttpVerb.Get, "api/orders/confirmed")]
                [RequirePermission("sales.order.read")]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        var generated = Endpoint(sources, "ConfirmedQuery");

        generated.Should().NotBeNull();
        generated!.Should().Contain("sales.order.read");
    }

    /// <summary>The specification's parameters become the route's query string.</summary>
    /// <remarks>
    ///     Without this the route would answer the rule with its arguments unset — the shape the
    ///     scaffolded search had, where the query declared filters and the endpoint bound none of them.
    /// </remarks>
    [Fact]
    public void TheSpecificationsParameters_BecomeQueryStringValues()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            public static class OrderSpecs
            {
                [Query<Order>]
                [Endpoint(HttpVerb.Get, "api/orders/by-code")]
                public static Specification<Order> WithCode(string code)
                    => Spec<Order>.Where(o => o.Code == code);
            }
            """);

        var generated = Endpoint(sources, "WithCodeQuery");

        generated.Should().NotBeNull();
        generated!.Should().Contain("\"code\"", "the parameter is read from the query string");
    }

    /// <summary>
    ///     The other half of the pair: a specification that declares no route gets none.
    /// </summary>
    /// <remarks>
    ///     Promotion to a route is opt-in on top of promotion to a query. Without this case "the route is
    ///     generated" would be satisfied by generating one for every specification a module declares —
    ///     routes nobody asked for, on paths the generator would have to invent.
    /// </remarks>
    [Fact]
    public void ASpecificationWithoutARoute_GetsNone()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            public static class OrderSpecs
            {
                [Query<Order>]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        Endpoint(sources, "ConfirmedQuery").Should().BeNull(
            "a specification that declares no route is a rule, not an operation");
    }

    /// <summary>
    ///     <c>[Endpoint]</c> on a member nothing derives from is reported, not ignored.
    /// </summary>
    /// <remarks>
    ///     The attribute's targets were widened for the specification's sake. Widening them without
    ///     saying anything is how a declaration becomes decoration: the author writes a route, the build
    ///     is green, and nothing is mapped.
    /// </remarks>
    [Fact]
    public void AnEndpointOnAMemberNothingDerivesFrom_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            public static class OrderSpecs
            {
                [Endpoint(HttpVerb.Get, "api/orders/nowhere")]
                public static string NotASpecification() => "";
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0525",
            "a route declared where nothing reads it is not a route");
    }

    /// <summary>The control: on a promoted specification the same attribute is silent.</summary>
    [Fact]
    public void AnEndpointOnAPromotedSpecification_IsSilent()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            public static class OrderSpecs
            {
                [Query<Order>]
                [Endpoint(HttpVerb.Get, "api/orders/confirmed")]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0525",
            "otherwise the diagnostic would forbid the one shape it exists to allow");
    }

    /// <summary>The generated endpoint of the named derived query, if there is one.</summary>
    private static string? Endpoint(Dictionary<string, string> sources, string queryTypeName)
        => sources
            .Where(pair => pair.Key.Contains(queryTypeName) && pair.Key.EndsWith(".Endpoint.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();
}
