using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Identity;
using Pragmatic.Actions.Invoker;
using Showcase.Catalog;
using Showcase.Catalog.Amenities.Actions;
using Showcase.Catalog.Amenities.Mutations;
using Showcase.Catalog.Enums;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     Executed end-to-end test of the declarative mutation-step <c>[CompositeAction]</c>.
///     Resolves the SG-generated invoker from the real host DI (proving the CompositeInvoker and its
///     step invokers are wired) and runs it against PostgreSQL (proving the single-transaction
///     semantics: all steps commit atomically, a failing step rolls back the whole composite).
/// </summary>
/// <remarks>
///     The second half runs the same composite over HTTP. That path was the one nobody had exercised:
///     a mutation-typed property is an ordinary body property, so the generated request body nests one
///     JSON object per step — and whether a caller can actually reach a composite through its endpoint
///     was an assumption, not a tested fact, until these three.
/// </remarks>
public class StepsCompositeActionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CompositeAction_AllStepsValid_CommitsAllInOneTransaction()
    {
        var prefix = UniquePrefix();

        var result = await InvokeCompositeAsync(new CreateAmenityPairAction
        {
            First = new CreateAmenityMutation { Name = $"{prefix}-A", Category = AmenityCategory.Spa },
            Second = new CreateAmenityMutation { Name = $"{prefix}-B", Category = AmenityCategory.Pool }
        });

        result.IsSuccess.Should().BeTrue("both steps are valid, so the composite commits");

        var items = await SearchAmenitiesByNameAsync(prefix);
        items.GetArrayLength().Should().Be(2,
            "both amenities are persisted by the composite's single SaveChanges");
    }

    [Fact]
    public async Task CompositeAction_SecondStepInvalid_RollsBackEntireComposite()
    {
        var prefix = UniquePrefix();

        var result = await InvokeCompositeAsync(new CreateAmenityPairAction
        {
            First = new CreateAmenityMutation { Name = $"{prefix}-A", Category = AmenityCategory.Spa },
            // Empty Name fails the entity's [Required] L2 validation → the second step fails.
            Second = new CreateAmenityMutation { Name = "", Category = AmenityCategory.Pool }
        });

        result.IsFailure.Should().BeTrue("a failing step must fail the whole composite");

        var items = await SearchAmenitiesByNameAsync(prefix);
        items.GetArrayLength().Should().Be(0,
            "the composite never reaches SaveChanges, so the first amenity must NOT persist (atomic rollback)");
    }

    // ── the same composite, reached over HTTP ────────────────────────────────────────────────────

    [Fact]
    public async Task CompositeEndpoint_NestedMutationsInTheBody_CreatesBoth()
    {
        var prefix = UniquePrefix();

        var response = await PostAsync("/api/amenities/pairs", new
        {
            first = new { name = $"{prefix}-A", category = "Spa" },
            second = new { name = $"{prefix}-B", category = "Pool" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the action returns VoidResult, so the generated endpoint answers 204");

        var items = await SearchAmenitiesByNameAsync(prefix);
        items.GetArrayLength().Should().Be(2,
            "the handler hands both nested objects to the composite invoker, which commits once");
    }

    [Fact]
    public async Task CompositeEndpoint_OneStepInvalid_PersistsNeither()
    {
        var prefix = UniquePrefix();

        var response = await PostAsync("/api/amenities/pairs", new
        {
            first = new { name = $"{prefix}-A", category = "Spa" },
            second = new { name = "", category = "Pool" }
        });

        response.IsSuccessStatusCode.Should().BeFalse("a failing step fails the whole composite");

        var items = await SearchAmenitiesByNameAsync(prefix);
        items.GetArrayLength().Should().Be(0,
            "atomicity has to survive the HTTP path too — the first step must not be left behind");
    }

    [Fact]
    public async Task CompositeEndpoint_WithoutThePermission_Is403()
    {
        // The composite declares [RequirePermission] and its steps declare none, so this is the only
        // place a permission is checked. Here the caller holds nothing at all.
        using var client = CreateClientWithPermissions();

        var response = await client.PostAsJsonAsync("/api/amenities/pairs", new
        {
            first = new { name = "NOPE-A", category = "Spa" },
            second = new { name = "NOPE-B", category = "Pool" }
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Pragmatic.Result.VoidResult<Pragmatic.Result.IError>> InvokeCompositeAsync(
        CreateAmenityPairAction action)
    {
        using var scope = Services.CreateScope();
        GivePermissionsTo(scope, CatalogPermissions.Amenity.Create);
        var invoker = scope.ServiceProvider
            .GetRequiredService<IVoidDomainActionInvoker<CreateAmenityPairAction>>();
        return await invoker.InvokeAsync(action);
    }

    /// <summary>
    ///     Puts an authenticated principal on the scope, so an in-process invocation of a permissioned
    ///     action is authorized the same way a request is.
    /// </summary>
    /// <remarks>
    ///     <c>ICurrentUser</c> reads the principal off <c>IHttpContextAccessor</c>, and a scope created
    ///     from the root provider has no HTTP context — so without this every permissioned action fails
    ///     here for a reason that has nothing to do with what the test is about. The alternative,
    ///     dropping the composite's <c>[RequirePermission]</c>, would leave its endpoint unguarded:
    ///     the steps declare no permission of their own, so nothing else would be asked.
    /// </remarks>
    private static void GivePermissionsTo(IServiceScope scope, params string[] permissions)
    {
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var claims = new List<Claim>
        {
            new(options.UserIdClaimType, $"composite-test-{Guid.NewGuid():N}"),
            new(options.TenantClaimType, "test-tenant"),
        };
        claims.AddRange(permissions.Select(p => new Claim(options.PermissionClaimType, p)));

        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "CompositeTest")),
            };
    }

    private async Task<JsonElement> SearchAmenitiesByNameAsync(string name)
    {
        var response = await GetAsync<JsonElement>($"/api/amenities/search?name={name}");
        return response.GetProperty("items");
    }

    private static string UniquePrefix() => $"PAIR{Guid.NewGuid():N}"[..12];
}
