using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Context.Providers;
using Pragmatic.Logging.Providers;
using Pragmatic.Logging.Tests.Configuration;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     The thread properties describe the thread that logged the entry, while the machine and process
///     properties are still computed once.
/// </summary>
/// <remarks>
///     <para>
///         The manager cached the whole aggregate until a provider was registered or removed, thread
///         properties included. After the first enriched call, every entry carried that call's thread and
///         culture, whatever thread wrote it.
///     </para>
///     <para>
///         The two logging threads are kept alive together, because a managed thread id can be reused
///         once its thread has ended. Otherwise two different threads could show the same id and the test
///         would prove nothing.
///     </para>
/// </remarks>
[Collection(nameof(TheAmbientContextManager))]
public class ThreadContextIsReadPerCallTests
{
    [Fact]
    public void TwoThreads_EachEntryCarriesTheThreadThatLoggedIt()
    {
        ContextManager.Instance.RegisterProvider(new ThreadContextProvider());
        using var provider = EnrichingMemoryProvider();
        var logger = provider.CreateLogger("Threads");

        var (first, second) = LogFromTwoLiveThreads(() => logger.LogInformation("from a thread"));

        var threadIds = provider.GetLogEntries().Select(entry => entry.Properties["ThreadId"]).ToArray();
        threadIds.Should().BeEquivalentTo([(object?)first, second]);
    }

    /// <summary>
    ///     The filter's choice among the thread's properties is decided once, by position: each property the
    ///     filter names is written under its own name, and the others are not.
    /// </summary>
    [Fact]
    public void AnIncludeFilter_WritesTheThreadPropertiesItNamesAndNoOther()
    {
        ContextManager.Instance.RegisterProvider(new ThreadContextProvider());
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.IncludeContextEnrichment = true;
        config.ContextFilter.Mode = ContextFilterMode.Include;
        config.ContextFilter.PropertyNames = ["IsBackground", "CurrentUICulture"];
        using var provider = new PragmaticMemoryProvider("memory", config);

        provider.CreateLogger("Threads").LogInformation("filtered");

        var properties = provider.GetLogEntries().Should().ContainSingle().Which.Properties;
        properties["IsBackground"].Should().Be(Thread.CurrentThread.IsBackground);
        properties["CurrentUICulture"].Should().Be(Thread.CurrentThread.CurrentUICulture.Name);
        properties.Keys.Should().NotContain("ThreadId");
        properties.Keys.Should().NotContain("IsThreadPoolThread");
        properties.Keys.Should().NotContain("CurrentCulture");
    }

    [Fact]
    public void ManyEntries_TheStaticProviderIsAskedOnce()
    {
        var counting = new CountingProvider();
        ContextManager.Instance.RegisterProvider(counting);
        try
        {
            using var provider = EnrichingMemoryProvider();
            var logger = provider.CreateLogger("Threads");

            LogFromTwoLiveThreads(() =>
            {
                for (var i = 0; i < 10; i++)
                    logger.LogInformation("from a thread");
            });

            provider.GetLogEntries().Should().HaveCount(20);
            provider.GetLogEntries().Should().OnlyContain(entry => Equals(entry.Properties["Counted"], "yes"));
            counting.Calls.Should().Be(1);
        }
        finally
        {
            ContextManager.Instance.UnregisterProvider(counting.Name);
        }
    }

    [Fact]
    public void Manager_GetContextProperties_ReadsTheThreadOnEveryCallAndTheStaticProvidersOnce()
    {
        using var manager = new ContextManager(registerDefaultProviders: false);
        var counting = new CountingProvider();
        manager.RegisterProvider(counting);
        manager.RegisterProvider(new ThreadContextProvider());

        var seen = new List<object?>();
        var (first, second) = LogFromTwoLiveThreads(() =>
        {
            var properties = manager.GetContextProperties();
            lock (seen)
                seen.Add(properties["ThreadId"]);
        });

        seen.Should().BeEquivalentTo([(object?)first, second]);
        counting.Calls.Should().Be(1);
    }

    private static PragmaticMemoryProvider EnrichingMemoryProvider()
    {
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.IncludeContextEnrichment = true;
        config.ContextFilter.Mode = ContextFilterMode.All;
        return new PragmaticMemoryProvider("memory", config);
    }

    /// <summary>Runs <paramref name="log" /> on two threads that are both alive until both have run it.</summary>
    private static (int First, int Second) LogFromTwoLiveThreads(Action log)
    {
        using var firstLogged = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int firstId = 0, secondId = 0;

        var first = new Thread(() =>
        {
            firstId = Environment.CurrentManagedThreadId;
            log();
            firstLogged.Set();
            release.Wait();
        });
        var second = new Thread(() =>
        {
            secondId = Environment.CurrentManagedThreadId;
            log();
        });

        first.Start();
        firstLogged.Wait();
        second.Start();
        second.Join();
        release.Set();
        first.Join();

        firstId.Should().NotBe(secondId);
        return (firstId, secondId);
    }

    private sealed class CountingProvider() : ContextProviderBase("Counting")
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public override IReadOnlyDictionary<string, object?> GetContextProperties()
        {
            Interlocked.Increment(ref _calls);
            return new Dictionary<string, object?> { ["Counted"] = "yes" };
        }
    }
}
