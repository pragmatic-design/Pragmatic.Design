using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>A Pragmatic provider that consumes every entry it is given; see <see cref="EventConsumer" />.</summary>
/// <remarks>
///     Synchronous and not deferred, so the call takes the full pipeline: the message rendered, the
///     structured properties extracted, the scopes captured — what a provider that writes anywhere needs.
/// </remarks>
internal sealed class PragmaticConsumingProvider(string name, IPragmaticProviderConfiguration configuration, EventConsumer consumer)
    : PragmaticLoggerProviderBase(name, configuration)
{
    protected override void WriteLogCore(LogEntry logEntry)
    {
        consumer.Begin();
        consumer.Message(logEntry.Message);

        foreach (var property in logEntry.Properties)
        {
            if (!EventConsumer.IsLibraryMetadata(property.Key))
                consumer.Property(property.Key, property.Value);
        }

        if (logEntry.Scopes is { } scopes)
        {
            foreach (var property in scopes)
            {
                if (!EventConsumer.IsLibraryMetadata(property.Key))
                    consumer.Property(property.Key, property.Value);
            }
        }

        consumer.Exception(logEntry.Exception);
        consumer.Complete();
    }
}
