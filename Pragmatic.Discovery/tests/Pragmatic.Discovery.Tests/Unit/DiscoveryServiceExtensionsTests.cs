using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Tests.Unit;

public class DiscoveryServiceExtensionsTests
{
    [Fact]
    public void AddDiscovery_RegistersDiscoveryService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDiscovery();

        var provider = services.BuildServiceProvider();
        var service = provider.GetService<IDiscoveryService>();

        service.Should().NotBeNull();
    }

    [Fact]
    public void AddDiscovery_RegistersInMemoryBackendByDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDiscovery();

        var provider = services.BuildServiceProvider();
        var backend = provider.GetService<IDiscoveryBackend>();

        backend.Should().NotBeNull();
        backend.Should().BeOfType<InMemoryDiscoveryBackend>();
    }

    [Fact]
    public void AddDiscovery_RegistersDiscoveryOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDiscovery();

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<DiscoveryOptions>>();

        options.Should().NotBeNull();
        options!.Value.Should().NotBeNull();
    }

    [Fact]
    public void AddDiscovery_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDiscovery(opts =>
        {
            opts.ThrowOnValidationFailure = true;
            opts.AutoRegisterOnStartup = false;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value;

        options.ThrowOnValidationFailure.Should().BeTrue();
        options.AutoRegisterOnStartup.Should().BeFalse();
    }

    [Fact]
    public void AddDiscovery_CalledTwice_DoesNotDuplicate()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDiscovery();
        services.AddDiscovery();

        // TryAddSingleton prevents duplicates for backend and service
        var backendDescriptors = services.Where(d => d.ServiceType == typeof(IDiscoveryBackend)).ToList();
        backendDescriptors.Should().HaveCount(1);

        var serviceDescriptors = services.Where(d => d.ServiceType == typeof(IDiscoveryService)).ToList();
        serviceDescriptors.Should().HaveCount(1);
    }

    [Fact]
    public void UseDiscoveryBackend_ReplacesDefaultBackend()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDiscovery();
        services.UseDiscoveryBackend<FakeDiscoveryBackend>();

        var provider = services.BuildServiceProvider();
        var backend = provider.GetService<IDiscoveryBackend>();

        backend.Should().BeOfType<FakeDiscoveryBackend>();
    }

    [Fact]
    public void UseDiscoveryBackend_WithoutAddDiscovery_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        // UseDiscoveryBackend without AddDiscovery must throw — the guard surfaces the misconfiguration.
        var act = () => services.UseDiscoveryBackend<FakeDiscoveryBackend>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddDiscovery*");
    }

    /// <summary>Fake backend for testing custom backend registration.</summary>
    private sealed class FakeDiscoveryBackend : IDiscoveryBackend
    {
        public Task StoreAsync(Discovery.Models.HostTopologyInfo topology, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Discovery.Models.HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default)
            => Task.FromResult<Discovery.Models.HostTopologyInfo?>(null);

        public Task<IReadOnlyList<Discovery.Models.HostTopologyInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Discovery.Models.HostTopologyInfo>>(new List<Discovery.Models.HostTopologyInfo>());
    }
}
