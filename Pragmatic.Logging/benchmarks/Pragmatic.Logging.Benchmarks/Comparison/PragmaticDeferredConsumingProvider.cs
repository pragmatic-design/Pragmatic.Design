using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>
///     The consuming provider on the deferred path: the call's state read where it is and handed to the sink
///     directly, without a <see cref="LogEvent" /> between them.
/// </summary>
/// <remarks>
///     A probe, not the provider the comparison uses. Its row is the floor the shipped path, which fills a
///     reused <see cref="LogEvent" /> first, is read against.
/// </remarks>
internal sealed class PragmaticDeferredConsumingProvider(string name, IPragmaticProviderConfiguration configuration, EventConsumer consumer)
    : PragmaticLoggerProviderBase(name, configuration)
{
    protected internal override bool SupportsDeferredWrite => true;

    protected override void WriteLogCore(LogEvent logEvent) =>
        throw new InvalidOperationException("The deferred probe must not fall back to the classic path.");

    protected override void WriteLogCoreDeferred<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter, string category)
    {
        consumer.Begin();
        consumer.Message(formatter(state, exception));

        // Called on the interface without holding it, so a struct state is not boxed.
        if (state is IReadOnlyList<KeyValuePair<string, object?>>)
        {
            var count = ((IReadOnlyList<KeyValuePair<string, object?>>)state).Count;
            for (var i = 0; i < count; i++)
            {
                var property = ((IReadOnlyList<KeyValuePair<string, object?>>)state)[i];
                if (!EventConsumer.IsLibraryMetadata(property.Key))
                    consumer.Property(property.Key, property.Value);
            }
        }

        Providers.LoggerExternalScopeProvider.ForEachScope(static (scope, sink) =>
        {
            if (scope is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (!EventConsumer.IsLibraryMetadata(values[i].Key))
                        sink.Property(values[i].Key, values[i].Value);
                }
            }
            else if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (!EventConsumer.IsLibraryMetadata(pair.Key))
                        sink.Property(pair.Key, pair.Value);
                }
            }
        }, consumer);

        consumer.Exception(exception);
        consumer.Complete();
    }
}
