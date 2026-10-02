using Testcontainers.LocalStack;
using Xunit;

namespace Pragmatic.Configuration.Aws.Tests;

/// <summary>
///     LocalStack container emulating AWS Secrets Manager + SSM Parameter Store. Skips gracefully when Docker
///     is unavailable.
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    public const string AccessKey = "test";
    public const string SecretKey = "test";
    public const string Region = "us-east-1";

    private LocalStackContainer? _container;

    /// <summary>Edge endpoint URL, or <c>null</c> when Docker is unavailable (tests skip).</summary>
    public string? ServiceUrl { get; private set; }

    /// <summary>Startup error, when the container failed to start (diagnostics).</summary>
    public string? StartupError { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new LocalStackBuilder().Build();
            await _container.StartAsync();
            ServiceUrl = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            ServiceUrl = null;
            StartupError = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
