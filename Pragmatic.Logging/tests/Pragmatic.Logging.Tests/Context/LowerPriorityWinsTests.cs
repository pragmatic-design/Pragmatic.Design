using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Providers;
using Pragmatic.Logging.Tests.Configuration;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     When two context providers supply the same key, the one with the lower <c>Priority</c> value wins,
///     whether each is static or read per call, and whatever the order they were registered in.
/// </summary>
/// <remarks>
///     <para>
///         That is the documented rule, and the one the priority ranges are built on: application
///         overrides at 1-99, machine defaults at 1000 and above. The manager merged the providers in
///         ascending order and let each overwrite the last, so the highest value won. A tenant provider at
///         80 lost to anything above it.
///     </para>
///     <para>
///         Every case registers the two providers in both orders, so that a result that depends on
///         registration rather than priority cannot pass.
///     </para>
/// </remarks>
[Collection(nameof(TheAmbientContextManager))]
public class LowerPriorityWinsTests
{
    public static TheoryData<bool, bool> StaticOrPerCall => new()
    {
        { true, true },
        { true, false },
        { false, true },
        { false, false },
    };

    [Theory]
    [MemberData(nameof(StaticOrPerCall))]
    public void Manager_TheLowerPriorityValueWins(bool lowIsStatic, bool highIsStatic)
    {
        foreach (var lowFirst in new[] { true, false })
        {
            using var manager = new ContextManager(registerDefaultProviders: false);
            Register(manager, lowFirst, lowIsStatic, highIsStatic);

            manager.GetContextProperties()["Shared"].Should().Be("from 10",
                $"low static: {lowIsStatic}, high static: {highIsStatic}, low registered first: {lowFirst}");
        }
    }

    [Theory]
    [MemberData(nameof(StaticOrPerCall))]
    public void LoggingPath_TheEntryCarriesTheLowerPriorityValue(bool lowIsStatic, bool highIsStatic)
    {
        foreach (var lowFirst in new[] { true, false })
        {
            Register(ContextManager.Instance, lowFirst, lowIsStatic, highIsStatic);
            try
            {
                var config = PragmaticMemoryConfiguration.ForMemory();
                config.IncludeContextEnrichment = true;
                config.ContextFilter.Mode = ContextFilterMode.All;

                using var provider = new PragmaticMemoryProvider("memory", config);
                provider.CreateLogger("Priority").LogInformation("one entry");

                provider.GetLogEntries().Should().ContainSingle().Which.Properties["Shared"].Should().Be("from 10",
                    $"low static: {lowIsStatic}, high static: {highIsStatic}, low registered first: {lowFirst}");
            }
            finally
            {
                ContextManager.Instance.UnregisterProvider("Low");
                ContextManager.Instance.UnregisterProvider("High");
            }
        }
    }

    private static void Register(IContextManager manager, bool lowFirst, bool lowIsStatic, bool highIsStatic)
    {
        var low = new SharedKeyProvider("Low", 10, lowIsStatic);
        var high = new SharedKeyProvider("High", 500, highIsStatic);

        manager.RegisterProvider(lowFirst ? low : high);
        manager.RegisterProvider(lowFirst ? high : low);
    }

    private sealed class SharedKeyProvider(string name, int priority, bool isStatic) : ContextProviderBase(name, priority)
    {
        public override bool IsStatic => isStatic;

        public override IReadOnlyDictionary<string, object?> GetContextProperties()
            => new Dictionary<string, object?> { ["Shared"] = $"from {Priority}" };
    }
}
