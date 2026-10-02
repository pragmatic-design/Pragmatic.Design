using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Pragmatic.Configuration.Consul.Tests;

/// <summary>Consul dev-mode container (KV enabled). Skips gracefully when Docker is unavailable.</summary>
public sealed class ConsulFixture : IAsyncLifetime
{
    private IContainer? _container;

    /// <summary>Consul HTTP address, or <c>null</c> when Docker is unavailable (tests skip).</summary>
    public string? Address { get; private set; }

    /// <summary>Startup error, when the container failed to start (diagnostics).</summary>
    public string? StartupError { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder()
                .WithImage("hashicorp/consul:1.15")
                .WithPortBinding(8500, true)
                .WithCommand("agent", "-dev", "-client", "0.0.0.0")
                .WithWaitStrategy(
                    Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                        .ForPort(8500)
                        .ForPath("/v1/status/leader")
                        .ForResponseMessageMatching(async resp =>
                            (await resp.Content.ReadAsStringAsync()).Trim('"').Length > 0)))
                .Build();

            await _container.StartAsync();
            Address = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8500)}";
        }
        catch (Exception ex)
        {
            Address = null;
            StartupError = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
