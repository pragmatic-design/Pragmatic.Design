using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Configuration.Bridge;

/// <summary>
///     IConfigurationSource that reads from <see cref="IConfigurationStore"/>
///     via the cascade resolver and triggers reload on change notification.
///     This bridges the Pragmatic store abstraction into the standard
///     Microsoft.Extensions.Configuration pipeline, enabling IOptions{T} / IOptionsMonitor{T}.
/// </summary>
public sealed class PragmaticConfigurationSource : IConfigurationSource
{
    private readonly IConfigurationStore _store;
    private readonly EnvironmentProfile _environment;
    private readonly string? _keyPrefix;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly object _buildLock = new();
    private PragmaticConfigurationProvider? _provider;

    /// <summary>
    ///     Creates a configuration source backed by the Pragmatic store.
    /// </summary>
    /// <param name="store">The backing configuration store.</param>
    /// <param name="environment">Environment profile for cascade resolution.</param>
    /// <param name="keyPrefix">
    ///     Optional key prefix filter. When set, only keys starting with this prefix
    ///     are loaded. Useful for section-scoped providers.
    /// </param>
    /// <param name="loggerFactory">
    ///     Optional logger factory — when provided, hot-reload failures are logged
    ///     through the standard <see cref="ILogger"/> pipeline instead of silently
    ///     dropped to <see cref="System.Diagnostics.Debug"/>.
    /// </param>
    public PragmaticConfigurationSource(
        IConfigurationStore store,
        EnvironmentProfile environment,
        string? keyPrefix = null,
        ILoggerFactory? loggerFactory = null)
    {
        _store = store;
        _environment = environment;
        _keyPrefix = keyPrefix;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        // Cache the built provider: IConfigurationBuilder.Build() may invoke this
        // repeatedly, and a single backing provider keeps reload state coherent.
        if (_provider is not null)
            return _provider;

        lock (_buildLock)
        {
            return _provider ??= new PragmaticConfigurationProvider(
                _store, _environment, _keyPrefix,
                _loggerFactory?.CreateLogger<PragmaticConfigurationProvider>());
        }
    }
}
