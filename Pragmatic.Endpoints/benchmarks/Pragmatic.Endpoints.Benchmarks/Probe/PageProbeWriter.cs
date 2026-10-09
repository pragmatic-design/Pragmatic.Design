using System.Text.Json;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     The reservation page written as the generated writer writes it, with what the probe
///     takes out or does differently.
/// </summary>
internal static class PageProbeWriter
{
    public static void Page<TProbe>(Utf8JsonWriter writer, ReservationPage value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        if (value.Items is { } items)
        {
            writer.WritePropertyName(ProbeNames.Items);
            writer.WriteStartArray();
            foreach (var item in items)
            {
                if (item is { } summary)
                    Summary<TProbe>(writer, summary);
                else
                    writer.WriteNullValue();
            }

            writer.WriteEndArray();
        }

        if (TProbe.RawNumbers)
        {
            Span<byte> buffer = stackalloc byte[128];
            var block = new RawBlock(buffer);
            block.Bytes(ProbeNames.PageRaw);
            block.Number(value.Page);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.PageSizeRaw);
            block.Number(value.PageSize);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.TotalCountRaw);
            block.Number(value.TotalCount);
            writer.WriteRawValue(block.Written, skipInputValidation: true);
        }
        else
        {
            writer.WriteNumber(ProbeNames.Page, value.Page);
            writer.WriteNumber(ProbeNames.PageSize, value.PageSize);
            writer.WriteNumber(ProbeNames.TotalCount, value.TotalCount);
        }

        writer.WriteEndObject();
    }

    private static void Summary<TProbe>(Utf8JsonWriter writer, ReservationSummary value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        if (TProbe.RawEncoderFree)
        {
            Span<byte> buffer = stackalloc byte[512];
            var block = new RawBlock(buffer);
            block.Bytes(ProbeNames.IdRaw);
            block.String(value.Id);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.GuestIdRaw);
            block.String(value.GuestId);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.PropertyIdRaw);
            block.String(value.PropertyId);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.CheckInRaw);
            block.String(value.CheckIn);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.CheckOutRaw);
            block.String(value.CheckOut);
            if (value.ExpectedArrival is { } expectedArrival)
            {
                block.Byte((byte)',');
                block.Bytes(ProbeNames.ExpectedArrivalRaw);
                block.String(expectedArrival);
            }

            if (value.ActualArrival is { } actualArrival)
            {
                block.Byte((byte)',');
                block.Bytes(ProbeNames.ActualArrivalRaw);
                block.String(actualArrival);
            }

            block.Byte((byte)',');
            NumberRun(ref block, value);
            writer.WriteRawValue(block.Written, skipInputValidation: true);
        }
        else
        {
            writer.WriteString(ProbeNames.Id, value.Id);
            writer.WriteString(ProbeNames.GuestId, value.GuestId);
            writer.WriteString(ProbeNames.PropertyId, value.PropertyId);
            Date<TProbe>(writer, ProbeNames.CheckIn, value.CheckIn);
            Date<TProbe>(writer, ProbeNames.CheckOut, value.CheckOut);
            if (value.ExpectedArrival is { } expectedArrival)
                Date<TProbe>(writer, ProbeNames.ExpectedArrival, expectedArrival);
            if (value.ActualArrival is { } actualArrival)
                Date<TProbe>(writer, ProbeNames.ActualArrival, actualArrival);

            if (TProbe.RawNumbers)
            {
                Span<byte> buffer = stackalloc byte[128];
                var block = new RawBlock(buffer);
                NumberRun(ref block, value);
                writer.WriteRawValue(block.Written, skipInputValidation: true);
            }
            else
            {
                writer.WriteNumber(ProbeNames.NumberOfGuests, value.NumberOfGuests);
                writer.WriteNumber(ProbeNames.TotalAmount, value.TotalAmount);
            }
        }

        String<TProbe>(writer, ProbeNames.Currency, value.Currency);
        String<TProbe>(writer, ProbeNames.PropertyName, value.PropertyName);
        String<TProbe>(writer, ProbeNames.GuestFirstName, value.GuestFirstName);
        String<TProbe>(writer, ProbeNames.GuestLastName, value.GuestLastName);
        writer.WriteEndObject();
    }

    private static void NumberRun(ref RawBlock block, ReservationSummary value)
    {
        block.Bytes(ProbeNames.NumberOfGuestsRaw);
        block.Number(value.NumberOfGuests);
        block.Byte((byte)',');
        block.Bytes(ProbeNames.TotalAmountRaw);
        block.Number(value.TotalAmount);
    }

    private static void Date<TProbe>(Utf8JsonWriter writer, JsonEncodedText name, DateTimeOffset value)
        where TProbe : struct, IResponseProbe
    {
        if (TProbe.ConstantDates)
            writer.WriteString(name, ProbeNames.ConstantText);
        else
            writer.WriteString(name, value);
    }

    private static void String<TProbe>(Utf8JsonWriter writer, JsonEncodedText name, string? value)
        where TProbe : struct, IResponseProbe
    {
        if (value is null)
            return;

        if (TProbe.ConstantStrings)
            writer.WriteString(name, ProbeNames.ConstantText);
        else
            writer.WriteString(name, value);
    }
}
