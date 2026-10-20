using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Messaging.Sql;
using Pragmatic.Messaging.Sql.Entities;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>
///     The headers of a queued row, written by whichever service enqueued it (#158, #312). For any text the
///     consumer's context is built, or refused with a <see cref="JsonException" />, which the consumer nacks toward
///     the dead-letter table (<c>AMessageWhoseHeadersDoNotParse_IsDeadLettered_AndTheBatchGoesOn</c>).
/// </summary>
public class SqlHeadersPropertyTests
{
    private static readonly string[] Pieces = ["{", "}", "[", "]", "\"", ":", ",", "\"a\"", "\"b\"", "1", "null", "true", " ", "\\u0000", "\\"];

    private static readonly Arbitrary<string> Headers =
        Gen.Frequency(
                (3, Gen.Elements(Pieces).ListOf().Select(pieces => string.Concat(pieces))),
                (1, ArbMap.Default.ArbFor<string>().Generator))
            .ToArbitrary();

    [Fact]
    public void Any_headers_give_a_context_or_a_refusal()
        => Prop.ForAll(Headers, headers =>
        {
            var row = new TransportMessage { MessageId = "m", QueueName = "q", Payload = [], HeadersJson = headers };
            try
            {
                var context = SqlQueueConsumer.ToContext(row);
                return context.MessageId == "m";
            }
            catch (JsonException)
            {
                return true;
            }
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(2000));
}
