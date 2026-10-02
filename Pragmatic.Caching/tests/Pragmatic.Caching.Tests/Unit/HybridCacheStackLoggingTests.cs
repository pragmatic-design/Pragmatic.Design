using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Tests verifying the source-generated <c>[LoggerMessage]</c> output of <see cref="HybridCacheStack"/>.
/// </summary>
public sealed class HybridCacheStackLoggingTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CapturingLogger _logger;
    private readonly HybridCacheStack _sut;

    public HybridCacheStackLoggingTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        var hybridCache = _provider.GetRequiredService<HybridCache>();
        _logger = new CapturingLogger();
        _sut = new HybridCacheStack(hybridCache, _logger);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task GetOrSetAsync_LogsGetOrSetMessageWithKey()
    {
        await _sut.GetOrSetAsync<string>("log-getset", _ => ValueTask.FromResult("v"));

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("get-or-set") && e.Message.Contains("log-getset"));
    }

    [Fact]
    public async Task SetAsync_LogsSetMessageWithKey()
    {
        await _sut.SetAsync("log-set", 1);

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("Cache set") && e.Message.Contains("log-set"));
    }

    [Fact]
    public async Task RemoveAsync_LogsRemoveMessageWithKey()
    {
        await _sut.RemoveAsync("log-remove");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("Cache remove") && e.Message.Contains("log-remove"));
    }

    [Fact]
    public async Task InvalidateByTagAsync_LogsInvalidateTagAsInformation()
    {
        await _sut.InvalidateByTagAsync("log-tag");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("invalidate") && e.Message.Contains("log-tag"));
    }

    [Fact]
    public async Task InvalidateByTagsAsync_LogsTagCountAsInformation()
    {
        await _sut.InvalidateByTagsAsync(["a", "b", "c"]);

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("3 tag"));
    }

    [Fact]
    public async Task TryGetAsync_CacheMiss_LogsMissMessage()
    {
        await _sut.TryGetAsync<string>("log-miss");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("miss") && e.Message.Contains("log-miss"));
    }

    [Fact]
    public async Task TryGetAsync_CacheHit_LogsHitMessage()
    {
        await _sut.SetAsync("log-hit", "present");
        _logger.Entries.Clear();

        await _sut.TryGetAsync<string>("log-hit");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("hit") && e.Message.Contains("log-hit"));
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<HybridCacheStack>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
