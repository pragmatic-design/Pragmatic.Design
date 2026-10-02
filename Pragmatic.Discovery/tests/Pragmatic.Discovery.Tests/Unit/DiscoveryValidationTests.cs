using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Tests validation rules via DiscoveryService.ValidateAsync (since DiscoveryValidator is internal).
/// DISC001: Same module on different databases (Info)
/// DISC002: Same module, same database, different providers (Warning)
/// DISC003: ReadAccess coherence (Info)
/// </summary>
public class DiscoveryValidationTests
{
    private readonly InMemoryDiscoveryBackend _backend = new();

    private static HostTopologyInfo CreateTopology(
        string hostName,
        IReadOnlyList<ModuleDeploymentInfo>? modules = null,
        IReadOnlyList<BoundaryReadAccessInfo>? boundaries = null)
    {
        return new HostTopologyInfo
        {
            HostName = hostName,
            Modules = modules ?? [],
            Boundaries = boundaries ?? []
        };
    }

    private static ModuleDeploymentInfo Module(string name, string? database = null, string? provider = null)
    {
        return new ModuleDeploymentInfo
        {
            ModuleName = name,
            DatabaseName = database,
            Provider = provider
        };
    }

    private static BoundaryReadAccessInfo Boundary(string name, params string[] entityTypes)
    {
        return new BoundaryReadAccessInfo
        {
            BoundaryName = name,
            EntityTypes = entityTypes.ToList()
        };
    }

    private async Task<DiscoveryValidationResult> ValidateAsync(
        HostTopologyInfo incoming,
        params HostTopologyInfo[] existing)
    {
        // Register existing hosts in the backend, then validate via service
        foreach (var host in existing)
            await _backend.StoreAsync(host);

        var service = CreateService();
        return await service.ValidateAsync(incoming);
    }

    private IDiscoveryService CreateService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDiscovery();

        // Replace backend with our pre-populated one
        var descriptor = services.First(d => d.ServiceType == typeof(IDiscoveryBackend));
        services.Remove(descriptor);
        services.AddSingleton<IDiscoveryBackend>(_backend);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDiscoveryService>();
    }

    [Fact]
    public async Task Validate_WithNoConflicts_ReturnsValid()
    {
        var incoming = CreateTopology("HostA", [Module("ModuleA", "DbA", "SqlServer")]);
        var existing = CreateTopology("HostB", [Module("ModuleB", "DbB", "SqlServer")]);

        var result = await ValidateAsync(incoming, existing);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WithNoExistingHosts_ReturnsValid()
    {
        var incoming = CreateTopology("HostA", [Module("ModuleA", "DbA", "SqlServer")]);

        var result = await ValidateAsync(incoming);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DISC001_SameModuleDifferentDatabase_ReturnsInfoIssue()
    {
        var incoming = CreateTopology("HostA", [Module("SharedModule", "DbA", "SqlServer")]);
        var existing = CreateTopology("HostB", [Module("SharedModule", "DbB", "SqlServer")]);

        var result = await ValidateAsync(incoming, existing);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().ContainSingle(i => i.Code == "DISC001");
        result.Issues.First(i => i.Code == "DISC001").Severity.Should().Be(IssueSeverity.Info);
    }

    [Fact]
    public async Task Validate_DISC002_SameModuleSameDbDifferentProvider_ReturnsError()
    {
        var incoming = CreateTopology("HostA", [Module("SharedModule", "SharedDb", "SqlServer")]);
        var existing = CreateTopology("HostB", [Module("SharedModule", "SharedDb", "Postgres")]);

        var result = await ValidateAsync(incoming, existing);

        // The same database cannot use two providers — Error, so ThrowOnValidationFailure can block.
        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle(i => i.Code == "DISC002");
        result.Issues.First(i => i.Code == "DISC002").Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_DISC002_ErrorsAppearInErrorsProperty()
    {
        var incoming = CreateTopology("HostA", [Module("SharedModule", "SharedDb", "SqlServer")]);
        var existing = CreateTopology("HostB", [Module("SharedModule", "SharedDb", "Postgres")]);

        var result = await ValidateAsync(incoming, existing);

        result.Errors.Should().ContainSingle(e => e.Code == "DISC002");
    }

    [Fact]
    public async Task Validate_ReadAccessBoundary_DoesNotEmitDisc003()
    {
        // DISC003 (runtime ReadAccess→module heuristic) is retired — it false-positives on every
        // boundary because entity names never start with the suffixed module name. Keep it out.
        var incoming = CreateTopology(
            "HostA",
            [Module("BillingModule", "FinDb", "SqlServer")],
            [Boundary("Billing", "Property", "RoomType")]);

        var result = await ValidateAsync(incoming);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().NotContain(i => i.Code == "DISC003");
    }

    [Fact]
    public async Task Validate_DISC003_BoundaryWithEmptyReadAccess_NoIssue()
    {
        var incoming = CreateTopology(
            "HostA",
            [Module("BillingModule", "FinDb", "SqlServer")],
            [Boundary("Billing")]);

        var result = await ValidateAsync(incoming);

        result.Issues.Should().NotContain(i => i.Code == "DISC003");
    }

    [Fact]
    public async Task Validate_SelfComparison_IsSkipped()
    {
        // Register HostA, then validate HostA again (re-registration)
        var topology = CreateTopology("HostA", [Module("ModuleA", "DbA", "SqlServer")]);

        // Store incoming as existing too (simulates re-registration)
        await _backend.StoreAsync(topology);

        var service = CreateService();
        var result = await service.ValidateAsync(topology);

        // No DISC001 should fire for self-comparison
        result.Issues.Should().NotContain(i => i.Code == "DISC001");
        result.Issues.Should().NotContain(i => i.Code == "DISC002");
    }

    [Fact]
    public async Task Validate_SameModuleSameDbSameProvider_NoConflict()
    {
        var incoming = CreateTopology("HostA", [Module("SharedModule", "SharedDb", "SqlServer")]);
        var existing = CreateTopology("HostB", [Module("SharedModule", "SharedDb", "SqlServer")]);

        var result = await ValidateAsync(incoming, existing);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().NotContain(i => i.Code == "DISC001");
        result.Issues.Should().NotContain(i => i.Code == "DISC002");
    }

    [Fact]
    public async Task Validate_MultipleConflicts_ReturnsAllIssues()
    {
        var incoming = CreateTopology(
            "HostA",
            [
                Module("ModuleX", "DbX", "SqlServer"),
                Module("ModuleY", "SharedDb", "SqlServer")
            ],
            [Boundary("SomeBoundary", "Entity1")]);

        var existing = CreateTopology(
            "HostB",
            [
                Module("ModuleX", "DbZ", "Postgres"),
                Module("ModuleY", "SharedDb", "Postgres")
            ]);

        var result = await ValidateAsync(incoming, existing);

        // DISC001 for ModuleX (different db, Info), DISC002 for ModuleY (same db, different provider, Error).
        result.Issues.Should().Contain(i => i.Code == "DISC001");
        result.Issues.Should().Contain(i => i.Code == "DISC002");
    }
}
