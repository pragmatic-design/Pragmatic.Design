using System.Net;
using Conformance.Catalog;
using Conformance.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The <c>[assembly: PragmaticAutoDerivePermissions]</c> posture reaches a <c>[Query]</c>: «every
///     operation without a permission requires a derived one».
/// </summary>
/// <remarks>
///     <para>
///         <c>Conformance.Catalog</c> has the posture on and a read that declares nothing,
///         <c>SearchCatalogItemsQuery</c>. The name the posture gives it is
///         <c>catalog.catalog-items.search</c>, and the caller is the same in every case — authenticated by
///         the <c>X-User-*</c> headers — with the only difference of having that name or not.
///     </para>
///     <para>
///         ⚠️ A query is neither an action nor a mutation, and derivation must cover it: otherwise the route
///         is published with no requirement and answers 200 to anyone authenticated — exactly the shape of
///         a correctly authorized call. It would be the worst of its family: this is the one posture that
///         exists so that nothing fails open, and reads would be where it did.
///     </para>
///     <para>
///         The control is the second case: the same caller <b>with</b> the derived name reads. Without it, a
///         403 would measure a route closed to everyone, not a permission — and it also proves that the
///         name applied is the one the posture declares, not another.
///     </para>
/// </remarks>
public class TheDerivedPermissionOnARead(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string DerivedName = "catalog.catalog-items.search";

    private void AuthenticateAs(string userId, string? permissions)
    {
        Client.DefaultRequestHeaders.Add("X-User-Id", userId);
        Client.DefaultRequestHeaders.Add("X-User-Name", userId);
        if (permissions is not null)
            Client.DefaultRequestHeaders.Add("X-User-Permissions", permissions);
    }

    [Fact]
    public async Task AnAuthenticatedCallerWithoutTheDerivedName_IsRefused()
    {
        AuthenticateAs("reader-without", permissions: null);

        var response = await Client.GetAsync("/api/catalog-items");

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"expected 403 — the route requires {DerivedName} and the caller does not have it — received {(int)response.StatusCode}");
    }

    /// <summary>The control: the derived name is what opens the route.</summary>
    [Fact]
    public async Task TheSameCallerWithTheDerivedName_Reads()
    {
        AuthenticateAs("reader-with", DerivedName);

        var response = await Client.GetAsync("/api/catalog-items");

        // Through ReadAsync: a failure names the body, not only the code.
        var body = await ReadAsync(response);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, body.ValueKind);
    }

    /// <summary>
    ///     A name no catalog lists can only deny: the read's derived name is seeded in the permission
    ///     catalog like a mutation's.
    /// </summary>
    [Fact]
    public async Task TheDerivedName_IsInThePermissionCatalog()
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IPermissionCatalog>();

        var all = await catalog.GetAllPermissionsAsync();

        Assert.Contains(all, p => p.Name == DerivedName);
    }

    /// <summary>
    ///     The route is not the only door: <b>in-process</b>, through the boundary's facade, the same
    ///     derived name is required.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Writing the derived name only into <c>EndpointModel.Authorization</c> — the route's
    ///     configuration — would be enough only if a query had no invoker, so that the route was the only
    ///     place it could be protected. A query has one, so that same read would answer 403 over HTTP and
    ///     return the rows to anyone calling <c>catalog.SearchCatalogItems(…)</c>.
    /// </remarks>
    [Fact]
    public async Task InProcess_WithoutTheDerivedName_IsRefused()
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogActions>();

        var answer = await catalog.SearchCatalogItems();

        answer.IsFailure.Should().BeTrue(
            $"the caller is anonymous and the read requires {DerivedName}, on this door as on the other");
    }

    /// <summary>The control: inside the internal call the same facade reads.</summary>
    /// <remarks>
    ///     Without it, «the derived permission holds in-process too» would be satisfied by a facade that
    ///     refuses everyone — and the read by name would become unusable from inside the boundary, which is
    ///     the place it exists for.
    /// </remarks>
    [Fact]
    public async Task InAnInternalCall_TheSameReadAnswers()
    {
        await using var scope = Services.CreateAsyncScope();
        var callContext = scope.ServiceProvider.GetRequiredService<ICallContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogActions>();

        using (callContext.EnterInternalCall())
        {
            var answer = await catalog.SearchCatalogItems();

            answer.IsFailure.Should().BeFalse(
                "whoever is already inside the boundary answered for the permissions on the way in");
        }
    }

    /// <summary>
    ///     The second control: the explicit opt-out stays an opt-out. The catalog's mutation is
    ///     <c>[AllowAnonymous]</c> and the posture does not touch it.
    /// </summary>
    [Fact]
    public async Task TheAnonymousOperationBesideIt_StaysOpen()
    {
        var response = await PutAsync($"/api/catalog-items/{Guid.NewGuid()}", new { listPrice = 1.0m });

        Assert.False(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"[AllowAnonymous] leaves the posture: expected a response other than 401/403, received {(int)response.StatusCode}");
    }
}
