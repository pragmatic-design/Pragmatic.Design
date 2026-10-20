using System.Text;
using System.Text.Json;
using Pragmatic.Messaging.Sql;
using Pragmatic.Messaging.Sql.Entities;

namespace Pragmatic.Messaging.Fuzz;

/// <summary>
///     The headers of a queued row as the SQL consumer reads them into a message's context (#312). The row is
///     written by whichever service enqueued it.
/// </summary>
/// <remarks>
///     The declared outcomes are a context and a <see cref="JsonException" />, which the consumer nacks toward the
///     dead-letter table. Those return; anything else escapes and is a crash.
/// </remarks>
public static class SqlHeadersTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var row = new TransportMessage
        {
            MessageId = "fuzz", QueueName = "fuzz", Payload = [], HeadersJson = Encoding.UTF8.GetString(data),
        };

        try
        {
            SqlQueueConsumer.ToContext(row);
        }
        catch (JsonException)
        {
        }
    }
}
