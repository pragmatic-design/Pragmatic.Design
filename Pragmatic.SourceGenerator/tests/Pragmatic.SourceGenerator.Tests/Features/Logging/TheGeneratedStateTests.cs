using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>
///     What a generated call site hands the logger, read through every view a provider can use: the
///     formatter's text, the list of pairs, the UTF-8 message and the JSON properties.
/// </summary>
public class TheGeneratedStateTests
{
    private const string Source = """
        using System;
        using Microsoft.Extensions.Logging;
        using Pragmatic;
        using Pragmatic.Privacy;

        namespace Sample.Orders;

        public enum Channel { Web, Store }

        public static partial class OrderLog
        {
            [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Order {OrderId} placed for {Amount} via {Channel} at {PlacedAt:yyyy-MM-dd}")]
            public static partial void OrderPlaced(ILogger logger, int orderId, decimal amount, Channel channel, DateTime placedAt);

            [LoggerMessage(Level = LogLevel.Warning, Message = "Receipt for {OrderId} sent to {Email} with {Token}")]
            public static partial void ReceiptSent(ILogger logger, int orderId, [PersonalData(DataCategory.Contact)] string email, [NotLogged] string token);

            [LoggerMessage(Message = "Payment for {OrderId} failed {{retrying}}")]
            public static partial void PaymentFailed(ILogger logger, LogLevel level, Exception error, Guid orderId);

            [LoggerMessage(Level = LogLevel.Information, Message = "Shipped {Parcel}")]
            public static partial void Shipped(ILogger logger, Parcel parcel);

            [LoggerMessage(Level = LogLevel.Information, Message = "Paid from {Account}")]
            public static partial void Paid(ILogger logger, Iban account);
        }

        public sealed record Parcel(string Code);

        // An application type that formats itself: it could declare [PersonalData] members of its own.
        public readonly struct Iban(string value) : IUtf8SpanFormattable
        {
            public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
                => System.Text.Encoding.UTF8.TryGetBytes(value, destination, out bytesWritten);
            public override string ToString() => value;
        }
        """;

    private static readonly CompiledCallSites CallSites = new(Source);

    private const string Email = "alice@example.com";
    private const string Token = "tok_9f8e7d";

    [Fact]
    public void TheFormatterRendersTheMessageAsMicrosoftWould()
    {
        var logger = new CapturingLogger();

        CallSites.Call("Sample.Orders.OrderLog", "OrderPlaced", logger, 42, 19.99m, 1, new DateTime(2026, 10, 6, 9, 30, 0));

        logger.Message.Should().Be("Order 42 placed for 19.99 via Store at 2026-10-06");
        logger.Level.Should().Be(LogLevel.Information);
        logger.EventId.Id.Should().Be(1001);
        logger.EventId.Name.Should().Be("OrderPlaced");
    }

    [Fact]
    public void TheListViewCarriesEachPropertyAndTheTemplate()
    {
        var logger = new CapturingLogger();

        CallSites.Call("Sample.Orders.OrderLog", "OrderPlaced", logger, 42, 19.99m, 1, new DateTime(2026, 10, 6, 9, 30, 0));

        var pairs = ((IReadOnlyList<KeyValuePair<string, object?>>)logger.State!).ToList();
        pairs.Select(p => p.Key).Should().Equal("OrderId", "Amount", "Channel", "PlacedAt", "{OriginalFormat}");
        pairs[0].Value.Should().Be(42);
        pairs[1].Value.Should().Be(19.99m);
        pairs[4].Value.Should().Be("Order {OrderId} placed for {Amount} via {Channel} at {PlacedAt:yyyy-MM-dd}");
    }

    [Fact]
    public void TheUtf8ViewsWriteTheSameMessageAndTheProperties()
    {
        var logger = new CapturingLogger();

        CallSites.Call("Sample.Orders.OrderLog", "OrderPlaced", logger, 42, 19.99m, 1, new DateTime(2026, 10, 6, 9, 30, 0, DateTimeKind.Utc));

        var state = (IUtf8LogState)logger.State!;
        state.IsSelfContained.Should().BeTrue();
        Utf8Message(state).Should().Be(logger.Message);
        Json(state).Should().Be("""{"OrderId":42,"Amount":19.99,"Channel":"Store","PlacedAt":"2026-10-06T09:30:00.0000000Z"}""");
    }

    [Fact]
    public void AMaskedArgumentIsTheMaskInEveryView_AndInClearInNone()
    {
        var logger = new CapturingLogger();

        CallSites.Call("Sample.Orders.OrderLog", "ReceiptSent", logger, 42, Email, Token);

        var state = (IUtf8LogState)logger.State!;
        var pairs = ((IReadOnlyList<KeyValuePair<string, object?>>)logger.State!).ToList();
        string[] views =
        [
            logger.Message!,
            logger.State!.ToString()!,
            Utf8Message(state),
            Json(state),
            string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}")),
        ];

        foreach (var view in views)
        {
            view.Should().NotContain(Email).And.NotContain(Token);
            view.Should().Contain("[redacted]");
        }

        logger.Message.Should().Be("Receipt for 42 sent to [redacted] with [redacted]");
        Json(state).Should().Be("""{"OrderId":42,"Email":"[redacted]","Token":"[redacted]"}""");
    }

    [Fact]
    public void ALevelParameter_AnException_AndAnEscapedBraceReachTheLogger()
    {
        var logger = new CapturingLogger();
        var error = new InvalidOperationException("declined");
        var orderId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        CallSites.Call("Sample.Orders.OrderLog", "PaymentFailed", logger, LogLevel.Error, error, orderId);

        logger.Level.Should().Be(LogLevel.Error);
        logger.Exception.Should().BeSameAs(error);
        logger.Message.Should().Be("Payment for 6f9619ff-8b86-d011-b42d-00cf4fc964ff failed {retrying}");
        // No EventId set: derived from the event name, the same on every build.
        logger.EventId.Id.Should().Be(StableEventId("PaymentFailed"));
    }

    [Fact]
    public void BelowTheEnabledLevel_NothingIsLogged()
    {
        var logger = new CapturingLogger(LogLevel.Error);

        CallSites.Call("Sample.Orders.OrderLog", "OrderPlaced", logger, 42, 19.99m, 0, DateTime.UnixEpoch);

        logger.Calls.Should().Be(0);
    }

    [Fact]
    public void AnArgumentTheStateCannotWrite_SendsTheProviderToTheListView()
    {
        var logger = new CapturingLogger();
        var parcel = Activator.CreateInstance(CallSitesType("Sample.Orders.Parcel"), "PX-1");

        CallSites.Call("Sample.Orders.OrderLog", "Shipped", logger, parcel);

        var state = (IUtf8LogState)logger.State!;
        state.IsSelfContained.Should().BeFalse();
        ((Action)(() => Json(state))).Should().Throw<InvalidOperationException>();
        logger.Message.Should().Be("Shipped Parcel { Code = PX-1 }");
    }

    /// <summary>
    ///     Only the runtime's formattable types write themselves. An application's could hold a member it
    ///     declared personal; through the list view the declared redactor sees it.
    /// </summary>
    [Fact]
    public void AnApplicationTypeThatFormatsItself_IsNotWrittenByTheState()
    {
        var logger = new CapturingLogger();
        var account = Activator.CreateInstance(CallSitesType("Sample.Orders.Iban"), "IT60X0542811101000000123456");

        CallSites.Call("Sample.Orders.OrderLog", "Paid", logger, account);

        ((IUtf8LogState)logger.State!).IsSelfContained.Should().BeFalse();
    }

    private static Type CallSitesType(string name) => CallSites.Type(name);

    /// <summary>
    ///     FNV-1a over the name's UTF-16 code units, written out here rather than called: the test states
    ///     the id a call site gets, so a change to the derivation shows up as a red test, not as a new
    ///     expectation that follows it.
    /// </summary>
    private static int StableEventId(string name)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in name)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private static string Utf8Message(IUtf8LogState state)
    {
        Span<byte> buffer = stackalloc byte[256];
        state.TryFormatMessage(buffer, out var written).Should().BeTrue();
        return Encoding.UTF8.GetString(buffer[..written]);
    }

    private static string Json(IUtf8LogState state)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            state.WriteProperties(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
