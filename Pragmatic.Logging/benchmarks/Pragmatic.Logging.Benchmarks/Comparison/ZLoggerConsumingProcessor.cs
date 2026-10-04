using System.Buffers;
using ZLogger;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>A ZLogger processor that consumes every entry it is given; see <see cref="EventConsumer" />.</summary>
/// <remarks>
///     <para>
///         ZLogger calls <see cref="Post" /> on the logging thread, so the work happens inside the call
///         as it does for the other three. The message is rendered through ZLogger's own UTF-8 path into a
///         reused buffer, then decoded into the shared one.
///     </para>
///     <para>
///         ⚠️ Property values are read with <c>GetParameterValue(int)</c>, which boxes value types. ZLogger's
///         typed and JSON accessors avoid that, but the sink does not know the types, and neither do the
///         other three.
///     </para>
/// </remarks>
internal sealed class ZLoggerConsumingProcessor(EventConsumer consumer) : IAsyncLogProcessor
{
    private readonly ArrayBufferWriter<byte> _utf8 = new(512);

    public void Post(IZLoggerEntry log)
    {
        try
        {
            consumer.Begin();
            _utf8.ResetWrittenCount();
            log.ToString(_utf8);
            consumer.Utf8Message(_utf8.WrittenSpan);

            for (var i = 0; i < log.ParameterCount; i++)
            {
                var key = log.GetParameterKeyAsString(i);
                if (!EventConsumer.IsLibraryMetadata(key))
                    consumer.Property(key, log.GetParameterValue(i));
            }

            var info = log.LogInfo;
            if (info.ScopeState is { IsEmpty: false } scope)
            {
                foreach (var property in scope.Properties)
                {
                    if (!EventConsumer.IsLibraryMetadata(property.Key))
                        consumer.Property(property.Key, property.Value);
                }
            }

            consumer.Exception(info.Exception);
            consumer.Complete();
        }
        finally
        {
            log.Return();
        }
    }

    public ValueTask DisposeAsync() => default;
}
