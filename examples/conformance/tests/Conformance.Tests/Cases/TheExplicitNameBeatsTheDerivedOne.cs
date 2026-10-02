using System.Net;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[ExplicitPermission]</c> under the derivation posture: the operation requires the name it
///     <b>declares</b>, not the one the posture would give it.
/// </summary>
/// <remarks>
///     <para>
///         <c>RepriceCatalogItemMutation</c> lives in <c>Conformance.Catalog</c>, where
///         <c>[assembly: PragmaticAutoDerivePermissions]</c> is on. <c>Reprice</c> is not among the verbs the
///         derivation recognizes, so the name it would get is <c>catalog.reprice-catalog-item</c>; the one it
///         declares is the entity's generated constant, <c>catalog.catalog-item.update</c>.
///     </para>
///     <para>
///         ⚠️ <b>The case that matters is the second</b>: the caller holding the <em>derived</em> name is
///         refused. Without it, «the declared name opens the route» is also satisfied by a replacement that
///         did not happen — because an operation requiring <i>both</i> names, or neither and only
///         authentication, would answer the first case the same way.
///     </para>
///     <para>
///         The row does not exist: authorization decides before the entity is loaded, so the expected
///         response is «anything but 401/403». It is the same shape as
///         <c>TheAnonymousOperationBesideIt_StaysOpen</c>, and for the same reason — measuring the gate, not
///         what sits behind it.
///     </para>
/// </remarks>
public class TheExplicitNameBeatsTheDerivedOne(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string DeclaredName = "catalog.catalog-item.update";
    private const string DerivedName = "catalog.reprice-catalog-item";

    private void AuthenticateAs(string userId, string? permissions)
    {
        Client.DefaultRequestHeaders.Add("X-User-Id", userId);
        Client.DefaultRequestHeaders.Add("X-User-Name", userId);
        if (permissions is not null)
            Client.DefaultRequestHeaders.Add("X-User-Permissions", permissions);
    }

    private Task<HttpResponseMessage> RepriceAsync()
        => PutAsync($"/api/catalog-items/{Guid.NewGuid()}/price", new { listPrice = 9.5m });

    [Fact]
    public async Task TheDeclaredName_OpensTheRoute()
    {
        AuthenticateAs("reprice-declared", DeclaredName);

        var response = await RepriceAsync();

        Assert.False(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"the mutation declares {DeclaredName} and the caller has it — received {(int)response.StatusCode}");
    }

    /// <summary>The control: the name the posture would have derived opens nothing.</summary>
    [Fact]
    public async Task TheDerivedName_OpensNothing()
    {
        AuthenticateAs("reprice-derived", DerivedName);

        var response = await RepriceAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"expected 403 — {DerivedName} is the name [ExplicitPermission] replaced — "
            + $"received {(int)response.StatusCode}");
    }

    /// <summary>
    ///     The second control: the replacement is not a waiver. An authenticated caller with neither name is
    ///     refused, which is what sets this cell apart from an <c>[AllowAnonymous]</c> written by mistake.
    /// </summary>
    [Fact]
    public async Task ACallerWithNeither_IsRefused()
    {
        AuthenticateAs("reprice-none", permissions: null);

        var response = await RepriceAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"expected 403 — the route requires {DeclaredName} — received {(int)response.StatusCode}");
    }
}
