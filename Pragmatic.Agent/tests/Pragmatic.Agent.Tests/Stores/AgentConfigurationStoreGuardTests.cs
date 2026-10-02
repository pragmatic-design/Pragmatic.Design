using System.Collections.Generic;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Client.Stores;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Agent.Tests.Stores;

/// <summary>
///     Verifies the sensitive-write guard in <see cref="AgentConfigurationStore"/>: writing a
///     <c>[Sensitive]</c> value as plaintext into the Agent KV (which gossips + persists it in the clear)
///     is warned about, while a <c>secret://</c> reference or a non-sensitive key is not.
/// </summary>
/// <remarks>
///     The store is constructed over an unconnected <see cref="AgentConnection"/> so writes take the local
///     fallback path — the guard runs before either branch, so no daemon is required.
/// </remarks>
public class AgentConfigurationStoreGuardTests
{
    [Fact]
    public async Task SetAsync_SensitivePlaintext_LogsWarning()
    {
        var (store, logs) = BuildStore("Db:Password");

        await store.SetAsync("Db:Password", "plaintext-secret");

        logs.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Db:Password"));
    }

    [Fact]
    public async Task SetAsync_SensitiveButSecretReference_DoesNotWarn()
    {
        var (store, logs) = BuildStore("Db:Password");

        await store.SetAsync("Db:Password", SecretReference.Create("db-password"));

        logs.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SetAsync_NonSensitiveKey_DoesNotWarn()
    {
        var (store, logs) = BuildStore("Db:Password");

        await store.SetAsync("Public:Setting", "plaintext");

        logs.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    private static (AgentConfigurationStore Store, List<LogEntry> Logs) BuildStore(params string[] sensitiveKeys)
    {
        var connection = new AgentConnection("pragmatic-agent-guard-test-nonexistent"); // never connected
        var logs = new List<LogEntry>();
        var store = new AgentConfigurationStore(
            connection,
            fallback: null,
            new FakeSensitiveKeyClassifier(sensitiveKeys),
            new CapturingLoggerFactory(logs));
        return (store, logs);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class FakeSensitiveKeyClassifier(string[] sensitiveKeys) : ISensitiveKeyClassifier
    {
        private readonly HashSet<string> _keys = new(sensitiveKeys, System.StringComparer.Ordinal);
        public bool IsSensitive(string key) => _keys.Contains(key);
    }

    private sealed class CapturingLoggerFactory(List<LogEntry> logs) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private sealed class CapturingLogger(List<LogEntry> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception,
            System.Func<TState, System.Exception?, string> formatter)
            => logs.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
