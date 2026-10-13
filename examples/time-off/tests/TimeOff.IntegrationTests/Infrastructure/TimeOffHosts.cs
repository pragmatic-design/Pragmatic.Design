namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     The hosts the test classes share: one for every class that changes nothing, and one per class that
///     changes the host's configuration or services. Built on first use, disposed with the database.
/// </summary>
/// <remarks>
///     xUnit creates a test class's instance for every test, so a host built in <c>InitializeAsync</c> is
///     a host per test: 165 of them, each starting the application. Kept here they start once per what
///     they differ by. Not synchronised: every class is in one collection, whose tests run one at a time.
/// </remarks>
public sealed class TimeOffHosts : IAsyncDisposable
{
    private readonly Dictionary<Type, TimeOffWebFactory> _hosts = [];

    /// <summary>The host kept under <paramref name="key" />, built by <paramref name="build" /> the first time.</summary>
    public TimeOffWebFactory For(Type key, Func<TimeOffWebFactory> build)
    {
        if (!_hosts.TryGetValue(key, out var host))
            _hosts[key] = host = build();
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts.Values)
            await host.DisposeAsync();
        _hosts.Clear();
    }
}
