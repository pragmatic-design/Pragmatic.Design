using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Identity.Local.Tests.Unit;

/// <summary>
///     <see cref="LocalIdentityOptions.PasswordWorkFactor" /> sets the cost of the hasher an application gets: the
///     one the generated registration resolves, not one built by hand.
/// </summary>
/// <remarks>
///     The option was documented and read by nothing: the container built the hasher with its constructor's
///     default, so an application that asked for 13 hashed at 12, and one that raised it never saw an old hash
///     ask to be upgraded. Low costs keep these fast; what is measured is that the configured one is used.
/// </remarks>
public sealed class TheConfiguredWorkFactorIsTheOneUsedTests
{
    [Fact]
    public void TheHashes_CarryTheConfiguredCost()
    {
        var hasher = HasherConfiguredWith(5);

        CostOf(hasher.Hash("password123")).Should().Be(5);
    }

    [Fact]
    public void WithNothingConfigured_TheCostIsTheDocumentedDefault()
    {
        var hasher = new ServiceCollection().AddOptions().AddPragmaticServices()
            .BuildServiceProvider().GetRequiredService<IPasswordHasher>();

        CostOf(hasher.Hash("password123")).Should().Be(12);
    }

    [Fact]
    public void AHashBelowTheConfiguredCost_AsksToBeUpgraded()
    {
        var older = HasherConfiguredWith(5).Hash("password123");

        HasherConfiguredWith(6).NeedsRehash(older).Should().BeTrue();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(32)]
    public void ACostBCryptDoesNotSupport_IsRefused_NamingTheOption(int workFactor)
    {
        var refusal = Record.Exception(() => HasherConfiguredWith(workFactor));

        refusal.Should().BeOfType<ArgumentOutOfRangeException>();
        ((ArgumentOutOfRangeException)refusal!).ParamName.Should().Be(nameof(LocalIdentityOptions.PasswordWorkFactor));
    }

    private static IPasswordHasher HasherConfiguredWith(int workFactor) =>
        new ServiceCollection()
            .Configure<LocalIdentityOptions>(options => options.PasswordWorkFactor = workFactor)
            .AddPragmaticServices()
            .BuildServiceProvider()
            .GetRequiredService<IPasswordHasher>();

    // $2a$05$… — the cost is the two digits between the second and third dollar sign.
    private static int CostOf(string hash) => int.Parse(hash.Split('$')[2], System.Globalization.CultureInfo.InvariantCulture);
}
