// Pragmatic.Composition.Tests - Host Wiring Generator Tests
// Both defects covered here live in the generated host, not in the templates: the runtime types
// (MaintenanceStep, ControlPlaneHealthCheck) compile, are tested, and are reachable from nothing.
// Asserting on a template renders the same blind spot the defects came from, so every assertion
// below reads the source the generator actually emitted for a host project.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Verifies that the generated host wires the host-level infrastructure it registers services for:
///     the maintenance 503 middleware (always) and the aggregated health check (opt-in).
/// </summary>
public class HostWiringGeneratorTests : CompositionGeneratorTestBase
{
    private const string MinimalHostSource = """
        namespace TestApp;

        public class Program { public static void Main() { } }
        """;

    private static string GeneratedServices()
    {
        var result = RunHostModeGenerator(MinimalHostSource);
        var services = GetGeneratedSource(result, "Host.Services");
        services.Should().NotBeNull("HOST mode must emit Host.Services.g.cs");
        return services!;
    }

    private static string GeneratedEntry()
    {
        var result = RunHostModeGenerator(MinimalHostSource);
        var entry = GetGeneratedSource(result, "Host.Entry");
        entry.Should().NotBeNull("HOST mode must emit Host.Entry.g.cs");
        return entry!;
    }

    /// <summary>
    ///     MaintenanceCommandHandler is registered, so an EnterMaintenanceCommand flips
    ///     IMaintenanceMode. Without MaintenanceStep in the pipeline the flag flips and every request
    ///     still goes through — an operator believes the host is drained when it is not.
    /// </summary>
    [Fact]
    public void HostServices_Always_RegisterMaintenanceStep()
    {
        GeneratedServices().Should().Contain(
            "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Hosting.MaintenanceStep>();");
    }

    /// <summary>
    ///     The 503 middleware takes MaintenanceModeOptions from DI. It was only ever registered by
    ///     UseMaintenanceMode(), so an always-on step would fail to construct in a host that never
    ///     calls it. The entry point registers the builder's own instance, which keeps the middleware
    ///     and the startup-failure page reading the same object.
    /// </summary>
    [Fact]
    public void HostEntry_Always_RegistersMaintenanceModeOptions()
    {
        GeneratedEntry().Should().Contain(
            "TryAddSingleton(builder.Services, pragmaticBuilder.Options.MaintenanceMode);");
    }

    /// <summary>
    ///     Contributors are collected by IHostHealthAggregator, and this bridge is how their verdict
    ///     reaches the health endpoint: Messaging deliberately writes no IHealthCheck of its own because
    ///     it relies on it.
    /// </summary>
    [Fact]
    public void HostEntry_RegistersControlPlaneHealthCheck()
    {
        GeneratedEntry().Should().Contain(
            "AddCheck<global::Pragmatic.Composition.ControlPlane.ControlPlaneHealthCheck>(");
    }

    /// <summary>
    ///     The entry point hands the decision to the mapper instead of taking half of it itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It emits no <c>if (Options.Health.Enabled)</c> around the call, because the mapper reads
    ///         the same flag — one condition in two places is a condition that will one day disagree with
    ///         itself. Whether to map, and whether the route is free, is one decision and lives in one
    ///         testable place.
    ///     </para>
    /// </remarks>
    [Fact]
    public void HostEntry_DelegatesTheHealthEndpointToTheMapper()
    {
        var entry = GeneratedEntry();

        entry.Should().Contain(
            "global::Pragmatic.Composition.Hosting.HealthEndpointMapper.Map(app, pragmaticBuilder.Options.Health);");
        // Counted, not searched for. The flag legitimately guards the *registration* of the check —
        // a different decision from mapping the route — so the string is expected once. What must not
        // come back is the second occurrence, around the call above, duplicating the condition the
        // mapper already applies.
        entry.Split("if (pragmaticBuilder.Options.Health.Enabled)").Length.Should().Be(2,
            "the flag guards the registration only; the mapper reads it itself for the route");
    }
}
