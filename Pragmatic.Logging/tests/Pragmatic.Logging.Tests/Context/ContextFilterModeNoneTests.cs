using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Context.Providers;
using Pragmatic.Logging.Providers;
using Pragmatic.Logging.Tests.Configuration;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     <c>ContextFilterMode.None</c> means what the enum says, "include no properties": an entry carries
///     what the call put on it and nothing from the ambient context or the context providers.
/// </summary>
/// <remarks>
///     The filter had no case for <c>None</c> and its default included everything, so a provider that
///     asked for no context got all of it. The control, with <c>All</c>, shows the same setup does put
///     context on the entry, so the empty result is the filter's doing.
/// </remarks>
[Collection(nameof(TheAmbientContextManager))]
public class ContextFilterModeNoneTests
{
    [Fact]
    public void ModeNone_TheEntryCarriesNoContextProperty()
    {
        var entry = LogOneEntry(ContextFilterMode.None);

        entry.Properties.Keys.Should().NotContain("Tenant");
        entry.Properties.Keys.Should().NotContain("ThreadId");
        entry.Properties.Keys.Should().NotContain("MachineName");
        entry.Properties["OrderId"].Should().Be(42);
    }

    [Fact]
    public void ModeAll_TheEntryCarriesTheContextProperties()
    {
        var entry = LogOneEntry(ContextFilterMode.All);

        entry.Properties["Tenant"].Should().Be("acme");
        entry.Properties.Keys.Should().Contain("ThreadId");
        entry.Properties.Keys.Should().Contain("MachineName");
        entry.Properties["OrderId"].Should().Be(42);
    }

    private static LogEntry LogOneEntry(ContextFilterMode mode)
    {
        ContextManager.Instance.RegisterProvider(new MachineContextProvider());
        ContextManager.Instance.RegisterProvider(new ThreadContextProvider());

        var config = PragmaticMemoryConfiguration.ForMemory();
        config.IncludeContextEnrichment = true;
        config.ContextFilter.Mode = mode;

        using var provider = new PragmaticMemoryProvider("memory", config);
        using (LogContextScope.PushProperty("Tenant", "acme"))
            provider.CreateLogger("Filter").LogInformation("Placed {OrderId}", 42);

        return provider.GetLogEntries().Should().ContainSingle().Which;
    }
}
