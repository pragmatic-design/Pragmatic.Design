using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Pragmatic.Logging.CallSites;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     The benchmark's call, <c>Order {OrderId} placed by {Customer} for {Amount}</c>, written by hand: as the
///     generated state writes it today (<see cref="ProbeVariant.HandTwice" />), and with each value formatted once
///     (<see cref="ProbeVariant.HandOnce" />).
/// </summary>
internal sealed partial class ProbeJsonProvider
{
    private static readonly JsonEncodedText OrderIdName = JsonEncodedText.Encode("OrderId");
    private static readonly JsonEncodedText CustomerName = JsonEncodedText.Encode("Customer");
    private static readonly JsonEncodedText AmountName = JsonEncodedText.Encode("Amount");

    // Instance fields, not constants: the JIT must format them, as it formats the generated state's.
    private readonly int _orderId = 42;
    private readonly string _customer = "jane";
    private readonly decimal _amount = 19.99m;
    private int _orderIdStart, _orderIdLength, _customerStart, _customerLength, _amountStart, _amountLength;

    // As the generated state renders it: literal, value, literal, value, literal, value.
    private ReadOnlySpan<byte> HandMessageTwice()
    {
        var written = 0;
        Utf8LogFormat.TryAppend(_message, ref written, "Order "u8);
        Utf8LogFormat.TryAppendFormatted(_message, ref written, _orderId, default);
        Utf8LogFormat.TryAppend(_message, ref written, " placed by "u8);
        Utf8LogFormat.TryAppend(_message, ref written, _customer);
        Utf8LogFormat.TryAppend(_message, ref written, " for "u8);
        Utf8LogFormat.TryAppendFormatted(_message, ref written, _amount, default);
        return _message.AsSpan(0, written);
    }

    // As the generated state writes them: every value formatted again.
    private void HandPropertiesTwice(Utf8JsonWriter writer)
    {
        writer.WriteNumber(OrderIdName, _orderId);
        writer.WriteString(CustomerName, _customer);
        writer.WriteNumber(AmountName, _amount);
    }

    // The same message, remembering where each value's bytes are.
    private ReadOnlySpan<byte> HandMessageOnce()
    {
        var written = 0;
        Utf8LogFormat.TryAppend(_message, ref written, "Order "u8);
        _orderIdStart = written;
        Utf8LogFormat.TryAppendFormatted(_message, ref written, _orderId, default);
        _orderIdLength = written - _orderIdStart;
        Utf8LogFormat.TryAppend(_message, ref written, " placed by "u8);
        _customerStart = written;
        Utf8LogFormat.TryAppend(_message, ref written, _customer);
        _customerLength = written - _customerStart;
        Utf8LogFormat.TryAppend(_message, ref written, " for "u8);
        _amountStart = written;
        Utf8LogFormat.TryAppendFormatted(_message, ref written, _amount, default);
        _amountLength = written - _amountStart;
        return _message.AsSpan(0, written);
    }

    // ,"@properties":{"OrderId":…,"Customer":"…","Amount":…} into the line itself, after the writer's bytes.
    private void HandRawProperties(ArrayBufferWriter<byte> line)
    {
        var destination = line.GetSpan(256);
        var written = 0;
        Copy(destination, ref written, ",\"@properties\":{\"OrderId\":"u8);
        Copy(destination, ref written, _message.AsSpan(_orderIdStart, _orderIdLength));
        Copy(destination, ref written, ",\"Customer\":\""u8);
        var customer = _message.AsSpan(_customerStart, _customerLength);
        if (JavaScriptEncoder.UnsafeRelaxedJsonEscaping.FindFirstCharacterToEncodeUtf8(customer) < 0)
        {
            Copy(destination, ref written, customer);
        }
        else
        {
            JavaScriptEncoder.UnsafeRelaxedJsonEscaping.EncodeUtf8(customer, destination[written..], out _, out var escaped);
            written += escaped;
        }

        Copy(destination, ref written, "\",\"Amount\":"u8);
        Copy(destination, ref written, _message.AsSpan(_amountStart, _amountLength));
        destination[written++] = (byte)'}';
        line.Advance(written);
    }

    // The whole line as bytes: what a provider writes when nothing in it needs the writer's escaping.
    private void HandAllRaw(ArrayBufferWriter<byte> line, ReadOnlySpan<byte> timestamp, byte[] levelAndLogger, byte[] eventBlock)
    {
        var message = HandMessageOnce();
        if (JavaScriptEncoder.UnsafeRelaxedJsonEscaping.FindFirstCharacterToEncodeUtf8(message) >= 0)
            throw new InvalidOperationException("The benchmark's message needs no escaping; this row assumes it.");

        var destination = line.GetSpan(512);
        var written = 0;
        Copy(destination, ref written, "{\"@timestamp\":\""u8);
        Copy(destination, ref written, timestamp);
        destination[written++] = (byte)'"';
        Copy(destination, ref written, levelAndLogger);
        Copy(destination, ref written, ",\"@message\":\""u8);
        Copy(destination, ref written, message);
        destination[written++] = (byte)'"';
        Copy(destination, ref written, eventBlock);
        line.Advance(written);
        HandRawProperties(line);
        line.Write("}"u8);
    }

    private static void Copy(Span<byte> destination, ref int written, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(destination[written..]);
        written += bytes.Length;
    }

    // The properties from the message's bytes: numbers raw, the string already UTF-8.
    private void HandPropertiesOnce(Utf8JsonWriter writer)
    {
        writer.WritePropertyName(OrderIdName);
        writer.WriteRawValue(_message.AsSpan(_orderIdStart, _orderIdLength), skipInputValidation: true);
        writer.WriteString(CustomerName, _message.AsSpan(_customerStart, _customerLength));
        writer.WritePropertyName(AmountName);
        writer.WriteRawValue(_message.AsSpan(_amountStart, _amountLength), skipInputValidation: true);
    }
}
