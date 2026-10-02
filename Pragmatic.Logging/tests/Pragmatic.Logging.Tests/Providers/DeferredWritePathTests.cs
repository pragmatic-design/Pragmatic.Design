using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
/// Semantics of the deferred (non-materializing) write path introduced for hot-path
/// performance: it must engage only when NO pipeline feature needs a materialized
/// LogEntry, and fall back transparently otherwise (so no feature can lose data).
/// </summary>
public class DeferredWritePathTests
{
    private sealed class RecordingProvider(IPragmaticProviderConfiguration configuration, bool supportsDeferred)
        : PragmaticLoggerProviderBase("recording", configuration)
    {
        public int DeferredWrites;
        public int MaterializedWrites;
        public LogEntry? LastEntry;
        public readonly List<KeyValuePair<string, object?>> AmbientScopesSeenDuringDeferredWrite = [];

        protected internal override bool SupportsDeferredWrite => supportsDeferred;

        protected override void WriteLogCoreDeferred<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter, string category)
        {
            DeferredWrites++;

            // The contract guarantees ambient scopes are still readable during a deferred write.
            Pragmatic.Logging.Providers.LoggerExternalScopeProvider.ForEachScope((scope, list) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> kvps)
                    list.AddRange(kvps);
            }, AmbientScopesSeenDuringDeferredWrite);
        }

        protected override void WriteLogCore(LogEntry logEntry)
        {
            MaterializedWrites++;
            LastEntry = logEntry;
        }
    }

    private static PragmaticProviderConfiguration LeanConfig()
    {
        var config = PragmaticProviderConfiguration.CreateDefault();
        config.MinimumLevel = LogLevel.Trace;
        config.IncludeStructuredProperties = false;
        config.IncludeContextEnrichment = false;
        return config;
    }

    [Fact]
    public void NoPipelineFeatures_TakesDeferredPath()
    {
        using var provider = new RecordingProvider(LeanConfig(), supportsDeferred: true);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("Order {OrderId}", 42);

        provider.DeferredWrites.Should().Be(1);
        provider.MaterializedWrites.Should().Be(0);
    }

    [Fact]
    public void ProviderWithoutDeferredSupport_TakesMaterializedPath()
    {
        using var provider = new RecordingProvider(LeanConfig(), supportsDeferred: false);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("Order {OrderId}", 42);

        provider.DeferredWrites.Should().Be(0);
        provider.MaterializedWrites.Should().Be(1);
        provider.LastEntry!.Message.Should().Be("Order 42");
    }

    [Fact]
    public void ActiveScope_DeferredWriteStillSeesAmbientScopes()
    {
        // Scopes are ambient (AsyncLocal) and the deferred write happens synchronously INSIDE
        // the scope, so a deferred sink that cares can enumerate them — no data can be lost.
        using var provider = new RecordingProvider(LeanConfig(), supportsDeferred: true);
        var logger = provider.CreateLogger("Test");

        using (logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = "acme" }))
        {
            logger.LogInformation("scoped message");
        }

        provider.DeferredWrites.Should().Be(1);
        provider.MaterializedWrites.Should().Be(0);
        provider.AmbientScopesSeenDuringDeferredWrite
            .Should().Contain(kvp => kvp.Key == "TenantId" && (string?)kvp.Value == "acme");
    }

    [Fact]
    public void ActiveScope_MaterializedPath_StillCapturesTheScopeOnTheEntry()
    {
        using var provider = new RecordingProvider(LeanConfig(), supportsDeferred: false);
        var logger = provider.CreateLogger("Test");

        using (logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = "acme" }))
        {
            logger.LogInformation("scoped message");
        }

        provider.MaterializedWrites.Should().Be(1);
        provider.LastEntry!.Scopes.Should().NotBeNull();
        provider.LastEntry.Scopes.Should().Contain(kvp => kvp.Key == "TenantId" && (string?)kvp.Value == "acme");
    }

    [Fact]
    public void ContextEnrichmentEnabled_FallsBackToMaterializedPath()
    {
        var config = LeanConfig();
        config.IncludeContextEnrichment = true;

        using var provider = new RecordingProvider(config, supportsDeferred: true);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("enriched message");

        provider.DeferredWrites.Should().Be(0, "context enrichment requires a materialized entry");
        provider.MaterializedWrites.Should().Be(1);
    }

    [Fact]
    public void DisabledLevel_WritesNothingOnEitherPath()
    {
        var config = LeanConfig();
        config.MinimumLevel = LogLevel.Warning;

        using var provider = new RecordingProvider(config, supportsDeferred: true);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("below minimum");

        provider.DeferredWrites.Should().Be(0);
        provider.MaterializedWrites.Should().Be(0);
    }
}
