using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Models;
using Xunit;

namespace Showcase.Tests.Unit;

/// <summary>
///     P3 drift guard: parses the <b>real</b> SG-emitted HostTopology of the Showcase host through
///     <see cref="HostTopologyInfo"/>. This is the seam that would have caught the <c>configKey</c> loss
///     and shape-mismatch crashes that hand-authored fakes never exercised.
/// </summary>
public class HostTopologyDiscoveryTests
{
    // Loading the host assembly runs the SG-emitted [ModuleInitializer] that registers the
    // AssemblyMetadataRegistry provider, and makes the emitted attribute available.
    private static readonly Assembly HostAssembly = Assembly.Load("Showcase.Host");

    [Fact]
    [SuppressMessage("Trimming", "IL2026", Justification = "Test reads the emitted attribute deliberately.")]
    public void FromAssembly_ParsesRealEmittedTopologyJson_IncludingConfigKey()
    {
        var topology = HostTopologyInfo.FromAssembly(HostAssembly);

        topology.Should().NotBeNull();
        topology!.HostName.Should().Be("Showcase.Host", WhyTheNameIsTheWholeOne);
        topology.Modules.Should().NotBeEmpty();
        // Proves the emitted configKey field is read back, not only emitted.
        topology.Modules.Should().Contain(m => m.ConfigKey != null);
    }

    [Fact]
    public void FromRegistry_ReturnsTopology_ViaGeneratedProvider()
    {
        // Force the SG-emitted [ModuleInitializer] to run: a plain Assembly.Load defers module
        // initialization until a type in the module is first touched, which a test may never do.
        RuntimeHelpers.RunModuleConstructor(HostAssembly.ManifestModule.ModuleHandle);

        var topology = HostTopologyInfo.FromRegistry();

        topology.Should().NotBeNull("the SG-emitted IAssemblyMetadataProvider registers the topology");
        topology!.HostName.Should().Be("Showcase.Host", WhyTheNameIsTheWholeOne);
    }

    /// <summary>
    ///     ⚠️ The host's name is the whole root namespace, not its last segment.
    /// </summary>
    /// <remarks>
    ///     Taken from the <b>last segment</b>, every host laid out as <c>App.Service.Host</c> would be
    ///     called <c>"Host"</c> — Casework's two services both would. Neither of these tests is about
    ///     the name: one proves <c>configKey</c> round-trips and the other that the emitted provider
    ///     registers at all, and the name is incidental to both. It is the whole root namespace
    ///     because a host's name has to tell it apart from the host beside it in the same process.
    /// </remarks>
    private const string WhyTheNameIsTheWholeOne =
        "the whole root namespace, not its last segment: 'Host' named every host in existence";
}
