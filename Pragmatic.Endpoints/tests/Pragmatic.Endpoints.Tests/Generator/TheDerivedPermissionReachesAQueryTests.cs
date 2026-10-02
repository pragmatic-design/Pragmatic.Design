using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The auto-derivation posture reaches a <c>[Query]</c>: a read that carries no permission requires
///     the derived one, exactly as an action or a mutation does.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A query is neither an action nor a mutation, so derivation that runs only over those two
///         models misses it. With the posture on and no <c>[AllowAnonymous]</c> on a query, the
///         generated route would carry <b>no</b> requirement at all — not a derived one, not any — and
///         the read would stay open to every authenticated caller. That is the worst of its family:
///         this is the one posture that exists so nothing fails open.
///     </para>
///     <para>
///         ⚠️ <b>The route is not the only door.</b> A query has an invoker, and the boundary facade
///         calls it in process, so the derived name has to reach the invoker as well — otherwise the
///         same read is refused over HTTP and served to anyone who asks the boundary for it. These
///         cases read the generated <i>endpoint</i>; the invoker half is
///         <c>TheDerivedPermissionReachesTheQueryInvokerTests</c> in
///         <c>Pragmatic.Persistence.EFCore.Tests</c>, which is where EF Core is referenced and the
///         invoker is therefore emitted.
///     </para>
///     <para>
///         Invisible because a route with no requirement answers 200, which is what a correctly
///         authorised call also looks like.
///     </para>
/// </remarks>
public class TheDerivedPermissionReachesAQueryTests : EndpointsGeneratorTestBase
{
    private const string OptIn = "[assembly: Pragmatic.Authorization.PragmaticAutoDerivePermissions]";

    /// <summary>The name the posture gives <c>SearchItemsQuery</c> in the Catalog boundary.</summary>
    private const string Derived = "\"catalog.items.search\"";

    private static string Source(string attributes, bool optIn = true) => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        {{(optIn ? OptIn : "")}}

        namespace TestApp.Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        public partial class Item : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; private set; } = "";
        }

        public sealed class ItemDto
        {
            public Guid Id { get; init; }
        }

        [Query<Item, ItemDto>]
        [Endpoint(HttpVerb.Get, "api/items")]
        {{attributes}}
        public partial class SearchItemsQuery
        {
            public string? Name { get; init; }
        }
        """;

    private static string TheEndpoint(string attributes, bool optIn = true)
    {
        var result = RunGeneratorWithPersistence(Source(attributes, optIn));
        var generated = GetGeneratedSource(result, "SearchItemsQuery.Endpoint");

        generated.Should().NotBeNull("the query endpoint is generated at all");

        return generated!;
    }

    /// <summary>The posture reaches the read.</summary>
    [Fact]
    public void FlagOn_AQueryWithoutPermission_RequiresTheDerivedName()
    {
        TheEndpoint("").Should().Contain(Derived,
            "a query under the posture requires the same shape of name an action does");
    }

    /// <summary>The control: an explicit opt-out is still an opt-out.</summary>
    /// <remarks>
    ///     Without it, "the derived name is emitted" would be satisfied by emitting it on every query —
    ///     which would close the public reads of every module that turns the posture on.
    /// </remarks>
    [Fact]
    public void FlagOn_AnAnonymousQuery_StaysOpen()
    {
        var endpoint = TheEndpoint("[AllowAnonymous]");

        endpoint.Should().Contain("AllowAnonymous()");
        endpoint.Should().NotContain(Derived, "[AllowAnonymous] opts out of the posture entirely");
    }

    /// <summary>The second control: off must mean untouched.</summary>
    [Fact]
    public void FlagOff_AQueryWithoutPermission_RequiresNothing()
    {
        TheEndpoint("", optIn: false).Should().NotContain(Derived,
            "with the switch off a query without [RequirePermission] requires nothing, as before");
    }

    /// <summary>A hand-written requirement wins over the derived one.</summary>
    [Fact]
    public void FlagOn_AQueryWithItsOwnPermission_KeepsIt()
    {
        var endpoint = TheEndpoint("[RequirePermission(\"catalog.items.audit\")]");

        endpoint.Should().Contain("\"catalog.items.audit\"");
        endpoint.Should().NotContain(Derived, "an author's choice is never swapped for a guess");
    }

    /// <summary>
    ///     A derived name that appears in no catalog can only deny: the query's name is seeded like an
    ///     action's, and reaches the manifest with its source.
    /// </summary>
    [Fact]
    public void FlagOn_TheDerivedQueryPermission_IsSeededAndListed()
    {
        var result = RunGeneratorWithPersistence(Source(""));

        GetGeneratedSource(result, "ActionPermissionCatalog").Should().NotBeNull()
            .And.Contain(Derived, "a role has to be able to grant what the posture requires");
        GetGeneratedSource(result, "PragmaticManifest").Should().NotBeNull()
            .And.Contain("\"name\":\"catalog.items.search\"")
            .And.Contain("\"source\":\"auto-derived\"");
    }
}
