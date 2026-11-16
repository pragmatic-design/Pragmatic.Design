using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Authorization.Management.Tests.Unit;

/// <summary>
///     A nullable dependency field of an action is optional (#812): the package's actions resolve in a host that registers no permission cache,
///     and <c>_permissionCache</c> is then null, which the actions read as "nothing cached to evict".
/// </summary>
/// <remarks>
///     The Showcase's three hosts all call <c>UsePermissionCache</c>, so none of them could see an invoker that required the cache.
/// </remarks>
public sealed class AnOptionalDependencyIsNotRequiredTests
{
    [Fact]
    public void DeactivateRole_ResolvesWithoutAPermissionCache()
    {
        using var services = AHostWithoutAPermissionCache().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
        using var scope = services.CreateScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<DeactivateRole>>();

        resolve.Should().NotThrow();
    }

    /// <summary>And it runs there: the role is deactivated, and with no cache there is nothing to evict.</summary>
    [Fact]
    public async Task DeactivateRole_SucceedsWithoutAPermissionCache()
    {
        var role = new DynamicRole { Name = "front-desk" };
        var store = new AuthorizationManagementStoreMock();
        store.GetRolesAsync.Returns(new ValueTask<IReadOnlyList<DynamicRole>>([role]));
        var host = AHostWithoutAPermissionCache();
        host.RemoveAll<IAuthorizationManagementStore>();
        host.AddScoped<IAuthorizationManagementStore>(_ => store);
        using var services = host.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
        using var scope = services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<DeactivateRole>>()
            .InvokeAsync(new DeactivateRole { RoleName = role.Name });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error?.ToString() : "");
        role.DeactivatedAt.Should().NotBeNull();
    }

    /// <summary>The control: a required dependency is still required, so the resolution above is not passing for lack of checking.</summary>
    [Fact]
    public void DeactivateRole_WithoutItsStore_DoesNotResolve()
    {
        var host = AHostWithoutAPermissionCache();
        host.RemoveAll<IAuthorizationManagementStore>();
        using var services = host.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
        using var scope = services.CreateScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<DeactivateRole>>();

        resolve.Should().Throw<InvalidOperationException>();
    }

    // The package's generated registration, with what every host provides (a store, the current user), and no UsePermissionCache.
    private static ServiceCollection AHostWithoutAPermissionCache()
    {
        var services = new ServiceCollection();
        Pragmatic.Authorization.Management.Actions.PragmaticActionsRegistrationExtensions.AddPragmaticActions(services);
        services.AddScoped<IAuthorizationManagementStore>(_ => new AuthorizationManagementStoreMock());
        services.AddScoped<ICurrentUser>(_ => new CurrentUserMock());
        return services;
    }
}
