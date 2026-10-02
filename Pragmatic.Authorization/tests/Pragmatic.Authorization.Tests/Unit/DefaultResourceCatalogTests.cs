using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Authorization.Serialization;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Tests.Unit;

public class DefaultResourceCatalogTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class FakeResourcePolicyStore(params ProtectedResource[] resources) : IResourcePolicyStore
    {
        private readonly Dictionary<string, PolicyExpression?> _policies = resources
            .Where(r => r.PolicyExpression is not null)
            .ToDictionary(r => r.Identifier, r => r.PolicyExpression, StringComparer.OrdinalIgnoreCase);

        public ValueTask<IReadOnlyList<ProtectedResource>> GetAllResourcesAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ProtectedResource>>(resources);

        public ValueTask<PolicyExpression?> GetPolicyAsync(string resourceIdentifier, CancellationToken ct = default)
            => ValueTask.FromResult(_policies.GetValueOrDefault(resourceIdentifier));
    }

    // =========================================================================
    // GetAllResourcesAsync
    // =========================================================================

    [Fact]
    public async Task GetAllResources_StaticOnly_ReturnsStaticResources()
    {
        var staticResources = new List<ProtectedResource>
        {
            new("Action", "Orders.PlaceOrder", "Place Order", "orders")
        };
        var catalog = new DefaultResourceCatalog(staticResources: staticResources);

        var result = await catalog.GetAllResourcesAsync();

        result.Should().HaveCount(1);
        result[0].Identifier.Should().Be("Orders.PlaceOrder");
    }

    [Fact]
    public async Task GetAllResources_NullStatic_ReturnsEmpty()
    {
        var catalog = new DefaultResourceCatalog();

        var result = await catalog.GetAllResourcesAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllResources_MergesStaticAndDynamic()
    {
        var staticResources = new List<ProtectedResource>
        {
            new("Action", "Orders.PlaceOrder", "Place Order", "orders")
        };
        var dynamicStore = new FakeResourcePolicyStore(
            new ProtectedResource("Endpoint", "/api/custom", "Custom", "custom"));

        var catalog = new DefaultResourceCatalog(
            staticResources: staticResources,
            dynamicStore: dynamicStore);

        var result = await catalog.GetAllResourcesAsync();

        result.Should().HaveCount(2);
    }

    // =========================================================================
    // GetResourcePolicyAsync
    // =========================================================================

    [Fact]
    public async Task GetResourcePolicy_StaticResourceWithPolicy_ReturnsPolicy()
    {
        var policy = new PolicyExpression { Type = PolicyExpressionType.Permission, Value = "orders.read" };
        var staticResources = new List<ProtectedResource>
        {
            new("Action", "Orders.PlaceOrder", "Place Order", "orders") { PolicyExpression = policy }
        };
        var catalog = new DefaultResourceCatalog(staticResources: staticResources);

        var result = await catalog.GetResourcePolicyAsync("Orders.PlaceOrder");

        result.Should().NotBeNull();
        result!.Type.Should().Be(PolicyExpressionType.Permission);
    }

    [Fact]
    public async Task GetResourcePolicy_FallsBackToDynamic()
    {
        var dynamicPolicy = new PolicyExpression { Type = PolicyExpressionType.Authenticated };
        var dynamicStore = new FakeResourcePolicyStore(
            new ProtectedResource("Endpoint", "/api/admin", "Admin", "admin")
            {
                PolicyExpression = dynamicPolicy
            });
        var catalog = new DefaultResourceCatalog(dynamicStore: dynamicStore);

        var result = await catalog.GetResourcePolicyAsync("/api/admin");

        result.Should().NotBeNull();
        result!.Type.Should().Be(PolicyExpressionType.Authenticated);
    }

    [Fact]
    public async Task GetResourcePolicy_NothingFound_ReturnsNull()
    {
        var catalog = new DefaultResourceCatalog();

        var result = await catalog.GetResourcePolicyAsync("nonexistent");

        result.Should().BeNull();
    }
}
