using NLog;
using NLog.Targets;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>An NLog target that consumes every event it is given; see <see cref="EventConsumer" />.</summary>
/// <remarks>
///     Through <see cref="TargetWithContext.GetAllProperties(LogEventInfo)" />, the way an NLog target
///     reads an event's properties together with the scope's.
/// </remarks>
internal sealed class NLogConsumingTarget : TargetWithContext
{
    private readonly EventConsumer _consumer;

    public NLogConsumingTarget(EventConsumer consumer)
    {
        _consumer = consumer;
        Name = "consuming";
        IncludeEventProperties = true;
        IncludeScopeProperties = true;
    }

    protected override void Write(LogEventInfo logEvent)
    {
        _consumer.Begin();
        _consumer.Message(logEvent.FormattedMessage);

        foreach (var property in GetAllProperties(logEvent))
        {
            if (!EventConsumer.IsLibraryMetadata(property.Key))
                _consumer.Property(property.Key, property.Value);
        }

        _consumer.Exception(logEvent.Exception);
        _consumer.Complete();
    }
}
