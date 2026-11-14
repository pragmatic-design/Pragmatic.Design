using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Providers;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     An unauthenticated caller holds the configured anonymous role and its permissions, through the same registration an application makes
///     (#722); an authenticated caller does not, and with none configured an anonymous caller holds nothing.
/// </summary>
public class AnAnonymousCallerHoldsTheAnonymousRoleTests
{
    // The options and the role store as AddPragmaticAuthorization registers them, read back from the registrations: what the builder was
    // told is what the resolver is given.
    private static IUserAuthorization AuthorizationOf(ICurrentUser user, string? anonymousRole)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => user); // the identity package's registration, which authorization decorates
        services.AddPragmaticAuthorization(a =>
        {
            a.AnonymousRole = anonymousRole;
            a.MapRole("public", r => r.WithPermissions("catalog.browse"));
            a.MapRole("member", r => r.WithPermissions("orders.place"));
        });

        var options = new AuthorizationOptions();
        foreach (var configure in services.Where(d => d.ServiceType == typeof(IConfigureOptions<AuthorizationOptions>)))
            ((IConfigureOptions<AuthorizationOptions>)configure.ImplementationInstance!).Configure(options);
        var store = (IRolePermissionStore)services.Single(d => d.ServiceType == typeof(IRolePermissionStore)).ImplementationInstance!;
        var wrapped = Options.Create(options);

        return new CachedPermissionResolver([new RoleExpansionProvider(store, wrapped)], user, wrapped);
    }

    [Fact]
    public async Task AnAnonymousCaller_HoldsTheAnonymousRolesPermissions()
    {
        var authorization = AuthorizationOf(AnonymousUser.Instance, "public");

        (await authorization.HasPermissionAsync("catalog.browse")).Should().BeTrue();
        (await authorization.HasPermissionAsync("orders.place")).Should().BeFalse("only the anonymous role's");
        authorization.IsInRole("public").Should().BeTrue("the role agrees with the permissions it grants");
    }

    [Fact]
    public async Task WithNoAnonymousRole_AnAnonymousCallerHoldsNothing()
    {
        var authorization = AuthorizationOf(AnonymousUser.Instance, anonymousRole: null);

        (await authorization.HasPermissionAsync("catalog.browse")).Should().BeFalse();
        authorization.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAuthenticatedCaller_DoesNotHoldTheAnonymousRole()
    {
        var authorization = AuthorizationOf(new Member(), "public");

        (await authorization.HasPermissionAsync("orders.place")).Should().BeTrue();
        (await authorization.HasPermissionAsync("catalog.browse")).Should().BeFalse("the anonymous role is an unauthenticated caller's");
        authorization.IsInRole("public").Should().BeFalse();
    }

    private sealed class Member : ICurrentUser
    {
        public string Id => "anna";

        public string? DisplayName => "anna";

        public bool IsAuthenticated => true;

        public PrincipalKind Kind => PrincipalKind.User;

        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } =
            new Dictionary<string, IReadOnlyList<string>> { ["role"] = ["member"] };

        public IDelegationContext? Delegation => null;

        public IUserAuthorization Authorization => AnonymousUser.Instance.Authorization;

        public IAuthenticationContext Authentication => AnonymousUser.Instance.Authentication;
    }
}
