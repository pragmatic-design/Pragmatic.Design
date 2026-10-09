using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Providers;
using Pragmatic.Logging.Tests.Configuration;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     The context filter's decisions are cached per property name; a new configuration starts them again,
///     so a property the old filter accepted is not written under the new one, nor the other way round.
/// </summary>
[Collection(nameof(TheAmbientContextManager))]
public class TheContextFilterFollowsTheConfigurationTests
{
    [Fact]
    public void AfterUpdateConfiguration_TheNewFilterDecides()
    {
        using var provider = new PragmaticMemoryProvider("memory", Including("Accepted"));
        var logger = provider.CreateLogger("Filter");

        using (LogContextScope.PushContext())
        {
            LogContextScope.Current!.SetProperty("Accepted", "a");
            LogContextScope.Current.SetProperty("Refused", "r");

            logger.LogInformation("before");
            provider.UpdateConfiguration(Including("Refused"));
            logger.LogInformation("after");
        }

        var entries = provider.GetLogEntries().ToArray();        entries.Should().HaveCount(2);
        entries[0].Properties.Keys.Should().Contain("Accepted");
        entries[0].Properties.Keys.Should().NotContain("Refused");
        entries[1].Properties.Keys.Should().Contain("Refused");
        entries[1].Properties.Keys.Should().NotContain("Accepted");
    }

    private static PragmaticProviderConfiguration Including(string name)
    {
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.IncludeContextEnrichment = true;
        config.ContextFilter.Mode = ContextFilterMode.Include;
        config.ContextFilter.PropertyNames = [name];
        return config;
    }
}
