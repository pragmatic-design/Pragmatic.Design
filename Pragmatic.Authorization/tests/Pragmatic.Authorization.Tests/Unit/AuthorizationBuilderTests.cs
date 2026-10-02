using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Tests.Unit;

public class AuthorizationBuilderTests
{
    private static AuthorizationBuilder CreateBuilder()
    {
        var services = new ServiceCollection();
        return new AuthorizationBuilder(services);
    }

    // =========================================================================
    // Tests — UseRolePermissionStore
    // =========================================================================

    [Fact]
    public void UseRolePermissionStore_SetsCustomRoleStoreFlag()
    {
        var builder = CreateBuilder();

        builder.UseRolePermissionStore<InMemoryRolePermissionStore>();

        builder.HasCustomRoleStore.Should().BeTrue();
    }

    // =========================================================================
    // Tests — UseGroupRoleStore
    // =========================================================================

    [Fact]
    public void UseGroupRoleStore_SetsCustomGroupStoreFlag()
    {
        var builder = CreateBuilder();

        builder.UseGroupRoleStore<InMemoryGroupRoleStore>();

        builder.HasCustomGroupStore.Should().BeTrue();
    }

    // =========================================================================
    // Tests — MapGroup
    // =========================================================================

    [Fact]
    public void MapGroup_CreatesInMemoryGroupStore()
    {
        var builder = CreateBuilder();

        builder.MapGroup("engineering", g => g.WithRoles("developer", "reviewer"));

        builder.InMemoryGroupStore.Should().NotBeNull();
    }

    [Fact]
    public async Task MapGroup_AddRolesToGroup()
    {
        var builder = CreateBuilder();

        builder.MapGroup("engineering", g => g.WithRoles("developer", "reviewer"));

        var roles = await builder.InMemoryGroupStore!.GetRolesForGroupAsync("engineering");
        roles.Should().Contain("developer").And.Contain("reviewer");
    }

    // =========================================================================
    // Tests — AddResourceAuthorizer
    // =========================================================================

    private sealed class TestAction;

    private sealed class TestAuthorizer : IResourceAuthorizer<TestAction>
    {
        public ValueTask<bool> CanAccessAsync(
            Pragmatic.Identity.ICurrentUser user, TestAction resource, string action,
            CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }

    [Fact]
    public void AddResourceAuthorizer_RegistersAuthorizerInterface()
    {
        var services = new ServiceCollection();
        var builder = new AuthorizationBuilder(services);

        builder.AddResourceAuthorizer<TestAuthorizer, TestAction>();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IResourceAuthorizer<TestAction>) &&
            sd.ImplementationType == typeof(TestAuthorizer));
    }

    // =========================================================================
    // The catalog of covered resource types
    // =========================================================================

    /// <summary>
    ///     Reads the catalog straight off its service descriptor — it is registered as a singleton
    ///     instance, so this is the object the container would hand out.
    /// </summary>
    private static IResourceAuthorizerCatalog ResolveCatalog(IServiceCollection services)
        => services.Single(sd => sd.ServiceType == typeof(IResourceAuthorizerCatalog))
               .ImplementationInstance as IResourceAuthorizerCatalog
           ?? throw new InvalidOperationException("IResourceAuthorizerCatalog is not registered as an instance.");

    [Fact]
    public void AddResourceAuthorizer_Reflectionless_RecordsTheResourceTypeInTheCatalog()
    {
        // The DI container cannot answer "was anything registered for a base of this type?" — it keys
        // by the closed generic. The catalog is what lets ResourceAuthorizationFilter tell an action
        // nobody meant to protect from one whose base has an authorizer DI will never return.
        var services = new ServiceCollection();

        new AuthorizationBuilder(services).AddResourceAuthorizer<TestAuthorizer, TestAction>();

        ResolveCatalog(services).Covers(typeof(TestAction)).Should().BeTrue();
    }

    [Fact]
    public void AddResourceAuthorizer_ByAuthorizerType_RecordsTheResourceTypeInTheCatalog()
    {
        var services = new ServiceCollection();

        new AuthorizationBuilder(services).AddResourceAuthorizer<TestAuthorizer, TestAction>();

        ResolveCatalog(services).Covers(typeof(TestAction)).Should().BeTrue();
    }

    [Fact]
    public void AddResourceAuthorizer_CalledTwice_KeepsOneCatalogWithBothTypes()
    {
        var services = new ServiceCollection();
        var builder = new AuthorizationBuilder(services);

        builder.AddResourceAuthorizer<TestAuthorizer, TestAction>();
        builder.AddResourceAuthorizer<OtherAuthorizer, OtherAction>();

        services.Count(sd => sd.ServiceType == typeof(IResourceAuthorizerCatalog)).Should().Be(1);
        var catalog = ResolveCatalog(services);
        catalog.Covers(typeof(TestAction)).Should().BeTrue();
        catalog.Covers(typeof(OtherAction)).Should().BeTrue();
    }

    [Fact]
    public void Catalog_DoesNotCoverAnUnregisteredType()
    {
        var services = new ServiceCollection();

        new AuthorizationBuilder(services).AddResourceAuthorizer<TestAuthorizer, TestAction>();

        ResolveCatalog(services).Covers(typeof(OtherAction)).Should().BeFalse();
    }

    private sealed class OtherAction;

    private sealed class OtherAuthorizer : IResourceAuthorizer<OtherAction>
    {
        public ValueTask<bool> CanAccessAsync(
            Pragmatic.Identity.ICurrentUser user, OtherAction resource, string action,
            CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }

    // =========================================================================
    // Tests — UsePermissionCache
    // =========================================================================

    [Fact]
    public void UsePermissionCache_WithTimeSpan_SetsCacheOptions()
    {
        var builder = CreateBuilder();

        builder.UsePermissionCache(TimeSpan.FromMinutes(10));

        builder.Options.CacheOptions.Should().NotBeNull();
        builder.Options.CacheOptions!.Expiration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void UsePermissionCache_WithAction_SetsCacheOptions()
    {
        var builder = CreateBuilder();

        builder.UsePermissionCache(opts =>
        {
            opts.Expiration = TimeSpan.FromMinutes(15);
            opts.KeyPrefix = "custom";
        });

        builder.Options.CacheOptions.Should().NotBeNull();
        builder.Options.CacheOptions!.Expiration.Should().Be(TimeSpan.FromMinutes(15));
        builder.Options.CacheOptions!.KeyPrefix.Should().Be("custom");
    }
}
