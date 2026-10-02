using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Assertions;
using Pragmatic.Identity;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

/// <summary>
///     <see cref="IdentityOptions" /> go through the options pattern, so whoever configures them later
///     is heard.
/// </summary>
/// <remarks>
///     <c>AddPragmaticIdentity()</c> without a configure — the call every generated host makes —
///     registered <c>Options.Create(new IdentityOptions())</c> as a fixed <c>IOptions&lt;IdentityOptions&gt;</c>.
///     A closed registration wins over the options pattern, so an authentication entry point that
///     aligned the name claim, and an application that set its own, were both ignored without a word.
/// </remarks>
public sealed class IdentityOptionsAreConfigurableTests
{
    [Fact]
    public void AConfigureRegisteredAfterward_IsApplied()
    {
        var services = new ServiceCollection();
        services.AddPragmaticIdentity();
        services.Configure<IdentityOptions>(o => o.PermissionClaimType = "perm");

        Resolve(services).PermissionClaimType.Should().Be("perm");
    }

    [Fact]
    public void AConfigurePassedIn_IsApplied()
    {
        var services = new ServiceCollection();
        services.AddPragmaticIdentity(o => o.TenantClaimType = "org");

        Resolve(services).TenantClaimType.Should().Be("org");
    }

    /// <summary>The control: nobody configures anything, and the defaults are the ones they were.</summary>
    [Fact]
    public void WithNobodyConfiguring_TheDefaultsStand()
    {
        var services = new ServiceCollection();
        services.AddPragmaticIdentity();

        var options = Resolve(services);

        options.UserIdClaimType.Should().Be(new IdentityOptions().UserIdClaimType);
        options.DisplayNameClaimType.Should().Be(new IdentityOptions().DisplayNameClaimType);
    }

    private static IdentityOptions Resolve(IServiceCollection services)
        => services.BuildServiceProvider().GetRequiredService<IOptions<IdentityOptions>>().Value;
}
