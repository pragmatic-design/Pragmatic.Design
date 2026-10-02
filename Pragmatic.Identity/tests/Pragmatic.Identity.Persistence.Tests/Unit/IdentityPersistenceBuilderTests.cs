using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity.Persistence.Stores;

namespace Pragmatic.Identity.Persistence.Tests.Unit;

public sealed class IdentityPersistenceBuilderTests
{
    [Fact]
    public void AddPragmaticIdentityPersistence_Default_RegistersEfStores()
    {
        var services = new ServiceCollection();

        services.AddPragmaticIdentityPersistence();

        var provider = services.BuildServiceProvider();
        // EF stores are registered but need a DbContext to resolve
        services.Should().Contain(sd => sd.ServiceType == typeof(IRolePermissionStore)
            && sd.ImplementationType == typeof(EfRolePermissionStore));
        services.Should().Contain(sd => sd.ServiceType == typeof(IGroupRoleStore)
            && sd.ImplementationType == typeof(EfGroupRoleStore));
    }

    [Fact]
    public void AddPragmaticIdentityPersistence_SkipRoleStore_DoesNotRegisterEfRoleStore()
    {
        var services = new ServiceCollection();

        services.AddPragmaticIdentityPersistence(b => b.SkipRolePermissionStore());

        services.Should().NotContain(sd => sd.ImplementationType == typeof(EfRolePermissionStore));
        // Group store should still be registered
        services.Should().Contain(sd => sd.ImplementationType == typeof(EfGroupRoleStore));
    }

    [Fact]
    public void AddPragmaticIdentityPersistence_SkipGroupStore_DoesNotRegisterEfGroupStore()
    {
        var services = new ServiceCollection();

        services.AddPragmaticIdentityPersistence(b => b.SkipGroupRoleStore());

        services.Should().NotContain(sd => sd.ImplementationType == typeof(EfGroupRoleStore));
        // Role store should still be registered
        services.Should().Contain(sd => sd.ImplementationType == typeof(EfRolePermissionStore));
    }

}
