using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Hosted service that registers all IContextProvider instances with the ContextManager.
/// </summary>
internal sealed class ContextProviderRegistrationService(
    IEnumerable<IContextProvider> contextProviders,
    ILogger<ContextProviderRegistrationService> logger,
    IContextManager? contextManager = null)
    : IHostedService
{
    private readonly IEnumerable<IContextProvider> _contextProviders = contextProviders ?? throw new ArgumentNullException(nameof(contextProviders));
    private readonly ILogger<ContextProviderRegistrationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    ///     The manager the container answers with, or the ambient one when nothing registered it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Naming <see cref="ContextManager.Instance" /> directly would leave an application that also
    ///     configures the logging builder with two managers carrying half the context each.
    ///     Resolving it makes the registration land wherever the composition decided —
    ///     and the composition puts the ambient manager in the container, because that is the one a log
    ///     provider reads.
    /// </remarks>
    private readonly IContextManager _contextManager = contextManager ?? ContextManager.Instance;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var contextManager = _contextManager;
        var registeredCount = 0;

        foreach (var provider in _contextProviders)
        {
            try
            {
                contextManager.RegisterProvider(provider);
                registeredCount++;

                _logger.LogDebug("Registered context provider: {ProviderName} (Priority: {Priority})",
                    provider.Name, provider.Priority);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register context provider: {ProviderName}",
                    provider.Name);
            }
        }

        _logger.LogInformation("Registered {Count} context providers for Pragmatic.Logging",
            registeredCount);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Context providers don't need explicit cleanup
        return Task.CompletedTask;
    }
}