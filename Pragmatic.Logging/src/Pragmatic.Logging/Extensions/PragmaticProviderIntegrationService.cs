using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Hosted service that integrates Pragmatic.Logging providers with the existing ILoggerFactory
/// when using augmentation mode. This allows Pragmatic providers to work alongside existing providers.
/// </summary>
internal sealed class PragmaticProviderIntegrationService(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for the application to fully initialize
        await Task.Yield();

        try
        {
            // Get the existing ILoggerFactory
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            if (loggerFactory == null)
                return;

            // Get the Pragmatic provider registry
            var providerRegistry = serviceProvider.GetService<PragmaticLoggerProviderRegistry>();
            if (providerRegistry == null)
                return;

            // Get all registered Pragmatic providers
            var pragmaticProviders = serviceProvider.GetServices<IPragmaticLoggerProvider>();

            // Register each Pragmatic provider as a standard ILoggerProvider bridge
            foreach (var pragmaticProvider in pragmaticProviders)
            {
                var bridgeProvider = new PragmaticProviderBridge(pragmaticProvider);
                loggerFactory.AddProvider(bridgeProvider);
            }
        }
        catch (Exception ex)
        {
            // Log to stderr so failures are visible without breaking application startup
            Console.Error.WriteLine($"[Pragmatic.Logging] Provider integration failed during startup: {ex}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup needed
        return Task.CompletedTask;
    }
}

/// <summary>
/// Bridge that adapts a Pragmatic.Logging provider to work as a standard ILoggerProvider.
/// This enables Pragmatic providers to be used in existing logging infrastructure.
/// </summary>
internal sealed class PragmaticProviderBridge(IPragmaticLoggerProvider pragmaticProvider) : ILoggerProvider
{
    private readonly IPragmaticLoggerProvider _pragmaticProvider = pragmaticProvider ?? throw new ArgumentNullException(nameof(pragmaticProvider));
    private volatile bool _disposed;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pragmaticProvider.CreateLogger(categoryName);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _pragmaticProvider.Dispose();
        }
    }
}