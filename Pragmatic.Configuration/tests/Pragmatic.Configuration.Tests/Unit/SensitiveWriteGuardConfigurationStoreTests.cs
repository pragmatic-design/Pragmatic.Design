using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Secrets;
using Xunit;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>
///     The general write-guard decorator warns on a <c>[Sensitive]</c> plaintext
///     write to any config backend, and passes non-sensitive / secret-reference writes through silently.
/// </summary>
public class SensitiveWriteGuardConfigurationStoreTests
{
    [Fact]
    public async Task SetAsync_SensitivePlaintext_WarnsAndStillWrites()
    {
        var (store, inner, logs) = Build("Db:Password");

        await store.SetAsync("Db:Password", "plaintext");

        logs.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Db:Password"));
        (await inner.GetAsync("Db:Password")).Should().Be("plaintext"); // advisory, not blocking
    }

    [Fact]
    public async Task SetAsync_SensitiveSecretReference_DoesNotWarn()
    {
        var (store, _, logs) = Build("Db:Password");

        await store.SetAsync("Db:Password", SecretReference.Create("db-password"));

        logs.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SetAsync_NonSensitiveKey_DoesNotWarn()
    {
        var (store, _, logs) = Build("Db:Password");

        await store.SetAsync("Public:Setting", "plaintext");

        logs.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    private static (SensitiveWriteGuardConfigurationStore Store, InMemoryConfigurationStore Inner, List<LogEntry> Logs) Build(
        params string[] sensitiveKeys)
    {
        var inner = new InMemoryConfigurationStore();
        var logs = new List<LogEntry>();
        var store = new SensitiveWriteGuardConfigurationStore(
            inner, new FakeClassifier(sensitiveKeys), new CapturingLogger(logs));
        return (store, inner, logs);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class FakeClassifier(string[] keys) : ISensitiveKeyClassifier
    {
        private readonly HashSet<string> _keys = new(keys, System.StringComparer.Ordinal);
        public bool IsSensitive(string key) => _keys.Contains(key);
    }

    private sealed class CapturingLogger(List<LogEntry> logs) : ILogger<SensitiveWriteGuardConfigurationStore>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception,
            System.Func<TState, System.Exception?, string> formatter)
            => logs.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
