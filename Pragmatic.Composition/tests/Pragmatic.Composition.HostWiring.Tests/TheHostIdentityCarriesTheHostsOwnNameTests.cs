// Pragmatic.Composition.HostWiring.Tests
// Asserts on the generated host, because the question is what the application ends up registering.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The generated host tells its identity its own name, rather than leaving it to be looked up in a
///     process-wide registry.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Left to itself, <c>LocalHostIdentity</c> reads the name from
///         <c>AssemblyMetadataRegistry.FindByCategory</c> — the <b>first</b> provider carrying a
///         topology — and the generator emits one provider per host from a <c>[ModuleInitializer]</c>.
///         So two hosts in one process would both report the name of whichever assembly the runtime
///         loaded first, and the second host's control-plane identity would be filed under its
///         neighbour's.
///     </para>
///     <para>
///         ⚠️ And the name has to <b>identify</b> the host, which is the part a passing test could
///         easily not measure: the last segment of the root namespace would call every host in the
///         conventional <c>App.Service.Host</c> layout <c>"Host"</c>, so passing each host "its own
///         name" would give them all the same one.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class TheHostIdentityCarriesTheHostsOwnNameTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    /// <summary>The identity is constructed with a name, not left to resolve one.</summary>
    /// <remarks>
    ///     Asserted on the <b>shape</b> and not on the probe's assembly name: what this holds is that the
    ///     host passes a name at all, and which name it is is the next test's subject.
    /// </remarks>
    [Fact]
    public void TheIdentity_IsGivenThisHostsName()
        => Identity().Should().MatchRegex(
            @"new global::Pragmatic\.Composition\.ControlPlane\.LocalHostIdentity\(hostName: ""[^""]+""\)",
            "the host's own name is a compile-time constant of the host, not the first entry of a "
            + "process-wide registry");

    /// <summary>
    ///     And it is the whole root namespace, which is what distinguishes two hosts in one process.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the control that stops the rule from being cosmetic. The probe host's namespace has
    ///     one segment, so it cannot show the difference by itself — what it can hold is the rule: the
    ///     name is not the fixed word every host would share. A host called <c>"Host"</c> passes
    ///     "each host is given a name" and fails the thing that matters: telling two hosts apart.
    /// </remarks>
    [Fact]
    public void TheNameIsNotTheWordEveryHostWouldShare()
        => Identity().Should().NotContain("hostName: \"Host\"",
            "the last segment of App.Service.Host is 'Host', which names every host in existence");

    private string Identity()
        => HostWiringFixture.LinesOf(fixture.Bare, HostServices)
            .Should().ContainSingle(line => line.Contains("LocalHostIdentity(", StringComparison.Ordinal))
            .Subject;
}
