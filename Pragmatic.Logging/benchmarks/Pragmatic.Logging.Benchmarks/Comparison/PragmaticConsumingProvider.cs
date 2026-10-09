using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>A Pragmatic provider that consumes every event it is given; see <see cref="EventConsumer" />.</summary>
/// <remarks>
///     Written as the shipped providers are, from <see cref="LogEvent" />: the message rendered, the structured
///     properties read out of the call's state, the scopes from the ambient stack — what a provider that writes
///     anywhere needs.
/// </remarks>
internal sealed class PragmaticConsumingProvider(string name, IPragmaticProviderConfiguration configuration, EventConsumer consumer)
    : PragmaticLoggerProviderBase(name, configuration)
{
    protected override void WriteLogCore(LogEvent logEvent)
    {
        consumer.Begin();
        consumer.Message(logEvent.Message);

        foreach (var property in logEvent.Properties)
        {
            if (!EventConsumer.IsLibraryMetadata(property.Key))
                consumer.Property(property.Key, property.Value);
        }

        foreach (var property in logEvent.Scopes)
        {
            if (!EventConsumer.IsLibraryMetadata(property.Key))
                consumer.Property(property.Key, property.Value);
        }

        consumer.Exception(logEvent.Exception);
        consumer.Complete();
    }
}
