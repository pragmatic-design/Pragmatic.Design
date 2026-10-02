using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Pragmatic.Configuration.Vault.Tests;

/// <summary>
///     HashiCorp Vault dev-mode container (KV v2 mounted at <c>secret/</c>, root token <c>root</c>). Skips
///     gracefully when Docker is unavailable.
/// </summary>
public sealed class VaultFixture : IAsyncLifetime
{
    private const string RootToken = "root";
    private IContainer? _container;

    /// <summary>Vault address, or <c>null</c> when Docker is unavailable (tests skip).</summary>
    public string? Address { get; private set; }

    /// <summary>Startup error, when the container failed to start (for diagnostics).</summary>
    public string? StartupError { get; private set; }

    /// <summary>The dev-mode root token.</summary>
    public const string Token = RootToken;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder()
                .WithImage("hashicorp/vault:1.15")
                .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", RootToken)
                .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
                .WithPortBinding(8200, true)
                .WithCommand("server", "-dev")
                .WithWaitStrategy(
                    Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8200).ForPath("/v1/sys/health")))
                .Build();

            await _container.StartAsync();
            Address = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8200)}";
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
