using Pragmatic.Testing.Assertions;
using Pragmatic.Http;
using Xunit;

namespace Pragmatic.Abstractions.Tests.Http;

/// <summary>
///     The addresses a server must refuse to fetch on someone else's instruction.
/// </summary>
public class OutboundUrlGuardTests
{
    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://example.com:8443/hook?x=1")]
    [InlineData("https://93.184.216.34/hook")]
    public void APublicHttpsUrl_IsAllowed(string url)
        => OutboundUrlGuard.Inspect(url).Should().Be(OutboundUrlVerdict.Allowed);

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    [InlineData("ftp://example.com")]
    public void ANonHttpsScheme_IsRefused(string url)
        => OutboundUrlGuard.Inspect(url).Should().Be(OutboundUrlVerdict.SchemeNotAllowed);

    [Fact]
    public void PlainHttp_IsAllowedOnlyWhenAskedFor()
    {
        OutboundUrlGuard.Inspect("http://example.com").Should().Be(OutboundUrlVerdict.SchemeNotAllowed);
        OutboundUrlGuard.Inspect("http://example.com", allowHttp: true).Should().Be(OutboundUrlVerdict.Allowed);
    }

    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://127.5.5.5/")]
    [InlineData("https://10.0.0.7/")]
    [InlineData("https://172.16.0.1/")]
    [InlineData("https://172.31.255.254/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://0.0.0.0/")]
    [InlineData("https://100.64.0.1/")]
    public void APrivateOrLoopbackAddress_IsRefused(string url)
        => OutboundUrlGuard.Inspect(url).Should().Be(OutboundUrlVerdict.ResolvesToInternalAddress);

    [Fact]
    public void TheCloudMetadataEndpoint_IsRefused()
    {
        // The single most valuable target of an SSRF: it hands out credentials to anything that asks
        // from the right position on the network.
        OutboundUrlGuard.Inspect("https://169.254.169.254/latest/meta-data/")
            .Should().Be(OutboundUrlVerdict.ResolvesToInternalAddress);
    }

    [Theory]
    [InlineData("https://[::1]/")]
    [InlineData("https://[fc00::1]/")]
    [InlineData("https://[fd12:3456::1]/")]
    [InlineData("https://[fe80::1]/")]
    public void AnInternalIPv6Address_IsRefused(string url)
        => OutboundUrlGuard.Inspect(url).Should().Be(OutboundUrlVerdict.ResolvesToInternalAddress);

    [Fact]
    public void AnIPv4MappedIPv6Loopback_IsRefused()
    {
        // ::ffff:127.0.0.1 is the same address wearing a different notation, and a check that only
        // looks at the IPv6 form lets it through.
        OutboundUrlGuard.Inspect("https://[::ffff:127.0.0.1]/")
            .Should().Be(OutboundUrlVerdict.ResolvesToInternalAddress);
    }

    [Theory]
    [InlineData("https://172.15.0.1/")]
    [InlineData("https://172.32.0.1/")]
    [InlineData("https://11.0.0.1/")]
    [InlineData("https://100.63.255.255/")]
    [InlineData("https://100.128.0.1/")]
    public void AddressesJustOutsideThePrivateRanges_AreAllowed(string url)
        => OutboundUrlGuard.Inspect(url).Should().Be(OutboundUrlVerdict.Allowed,
            "an over-broad guard blocks legitimate traffic and gets switched off");

    [Fact]
    public void CredentialsInTheUrl_AreRefused()
    {
        // https://real-host@attacker/ reads as the real host to a person and resolves to the attacker's
        // for a machine.
        OutboundUrlGuard.Inspect("https://trusted.example.com@attacker.test/")
            .Should().Be(OutboundUrlVerdict.CredentialsInUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void SomethingThatIsNotAnAbsoluteUrl_IsRefused(string? url)
        => OutboundUrlGuard.Inspect(url).Should().Be(OutboundUrlVerdict.NotAnAbsoluteUrl);

    [Fact]
    public async Task AHostThatCannotBeResolved_IsRefusedRatherThanAllowed()
    {
        // Defaulting to "allow" when the check cannot complete is how these guards stop working without
        // anyone noticing.
        var verdict = await OutboundUrlGuard.InspectResolvedAsync(
            new Uri("https://this-host-does-not-exist.invalid/"));

        verdict.Should().Be(OutboundUrlVerdict.HostCouldNotBeResolved);
    }

    [Fact]
    public async Task ALocalhostName_IsRefusedOnceResolved()
    {
        // The literal check cannot see this one: "localhost" is a name, and only resolution exposes it.
        var verdict = await OutboundUrlGuard.InspectResolvedAsync(new Uri("https://localhost/hook"));

        verdict.Should().Be(OutboundUrlVerdict.ResolvesToInternalAddress);
    }

    [Fact]
    public async Task ResolvingDoesNotRepeatWorkForALiteralAddress()
    {
        // A literal is decided without touching DNS, so an internal literal stays refused and a public
        // one stays allowed even with no resolver available.
        (await OutboundUrlGuard.InspectResolvedAsync(new Uri("https://10.0.0.1/")))
            .Should().Be(OutboundUrlVerdict.ResolvesToInternalAddress);

        (await OutboundUrlGuard.InspectResolvedAsync(new Uri("https://93.184.216.34/")))
            .Should().Be(OutboundUrlVerdict.Allowed);
    }

    // Hosts the resolver cannot find become a refusal rather than an exception, whatever shape the
    // failure takes. There is deliberately no case here for ArgumentException: every literal is decided
    // before DNS is touched, and a non-literal name fails as SocketException — measured across long,
    // underscored, hyphen-edged and non-ASCII hosts. The catch for it in the guard is defensive and
    // says so; asserting on it would mean asserting on something nothing can produce.
    [Theory]
    [InlineData("https://a_b/")]
    [InlineData("https://-x/")]
    public async Task HostThatCannotBeResolved_IsAVerdictRatherThanAnException(string url)
    {
        var act = () => OutboundUrlGuard.InspectResolvedAsync(new Uri(url)).AsTask();

        var verdict = await act.Should().NotThrowAsync();
        verdict.Subject.Should().Be(OutboundUrlVerdict.HostCouldNotBeResolved);
    }
}
