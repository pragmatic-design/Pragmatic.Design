using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Configuration.Resolution;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>
///     A second <c>AddPragmaticConfiguration</c> with options is refused, instead of being ignored.
/// </summary>
/// <remarks>
///     The resolver kept the options of the call that registered it, so a second call — turning the tenant
///     layer on, say — changed nothing and said nothing. A second call without options has nothing to lose
///     and stays a no-op, so two places that each make sure the services exist do not collide.
/// </remarks>
public class ASecondRegistrationDoesNotLoseItsOptionsTests
{
    [Fact]
    public void ASecondCallWithOptions_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration();

        var failed = Assert.Throws<InvalidOperationException>(
            () => services.AddPragmaticConfiguration(o => o.MultiTenant.Enabled = true));

        failed.Message.Should().Contain("AddPragmaticConfiguration");
    }

    /// <summary>The control: a second call with nothing to configure changes nothing and is allowed.</summary>
    [Fact]
    public void ASecondCallWithoutOptions_IsANoOp()
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration(o => o.EnvironmentTag = "eu");
        var count = services.Count;

        services.AddPragmaticConfiguration();

        services.Count.Should().Be(count);
        services.BuildServiceProvider().GetRequiredService<EnvironmentProfile>()
            .ResolutionChain.Should().Contain(tag => tag.EndsWith("-eu"));
    }
}
