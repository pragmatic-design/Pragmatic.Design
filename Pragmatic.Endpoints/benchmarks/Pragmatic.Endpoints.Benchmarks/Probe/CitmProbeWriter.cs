using System.Text.Json;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     <c>citm_catalog.json</c> written as the generated writer writes it, with what the probe
///     takes out or does differently.
/// </summary>
/// <remarks>
///     <see cref="PlainProbe" /> is the generated writer's code, call for call; <c>ProbeBenchmarks</c> checks it
///     writes the host's bytes, and so does every candidate row.
/// </remarks>
internal static class CitmProbeWriter
{
    // The benchmark writes on one thread; a generated writer would keep this per thread.
    private static readonly byte[] Scratch = new byte[64 * 1024];

    public static void Root<TProbe>(Utf8JsonWriter writer, CitmCatalog.Root value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        IntKeyedStrings<TProbe>(writer, ProbeNames.AreaNames, value.areaNames);
        IntKeyedStrings<TProbe>(writer, ProbeNames.AudienceSubCategoryNames, value.audienceSubCategoryNames);
        IntKeyedStrings<TProbe>(writer, ProbeNames.BlockNames, value.blockNames);
        IntKeyedStrings<TProbe>(writer, ProbeNames.SeatCategoryNames, value.seatCategoryNames);
        IntKeyedStrings<TProbe>(writer, ProbeNames.SubTopicNames, value.subTopicNames);
        IntKeyedStrings<TProbe>(writer, ProbeNames.SubjectNames, value.subjectNames);
        IntKeyedStrings<TProbe>(writer, ProbeNames.TopicNames, value.topicNames);

        if (value.topicSubTopics is { } topicSubTopics)
        {
            writer.WritePropertyName(ProbeNames.TopicSubTopics);
            if (!TProbe.RawEncoderFree || !TryRaw(writer, topicSubTopics))
            {
                writer.WriteStartObject();
                foreach (var entry in topicSubTopics)
                {
                    Key<TProbe>(writer, entry.Key);
                    if (entry.Value is { } ids)
                        Numbers<TProbe>(writer, ids);
                    else
                        writer.WriteNullValue();
                }

                writer.WriteEndObject();
            }
        }

        if (value.venueNames is { } venueNames)
        {
            writer.WritePropertyName(ProbeNames.VenueNames);
            writer.WriteStartObject();
            foreach (var entry in venueNames)
            {
                writer.WritePropertyName(entry.Key);
                StringValue<TProbe>(writer, entry.Value);
            }

            writer.WriteEndObject();
        }

        if (value.events is { } events)
        {
            writer.WritePropertyName(ProbeNames.Events);
            writer.WriteStartObject();
            foreach (var entry in events)
            {
                Key<TProbe>(writer, entry.Key);
                if (entry.Value is { } @event)
                    Event<TProbe>(writer, @event);
                else
                    writer.WriteNullValue();
            }

            writer.WriteEndObject();
        }

        if (value.performances is { } performances)
        {
            writer.WritePropertyName(ProbeNames.Performances);
            writer.WriteStartArray();
            foreach (var performance in performances)
            {
                if (performance is { } p)
                    Performance<TProbe>(writer, p);
                else
                    writer.WriteNullValue();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void IntKeyedStrings<TProbe>(Utf8JsonWriter writer, JsonEncodedText name, Dictionary<int, string>? value)
        where TProbe : struct, IResponseProbe
    {
        if (value is null)
            return;

        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach (var entry in value)
        {
            Key<TProbe>(writer, entry.Key);
            StringValue<TProbe>(writer, entry.Value);
        }

        writer.WriteEndObject();
    }

    private static void Event<TProbe>(Utf8JsonWriter writer, CitmCatalog.Event value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        writer.WriteNumber(ProbeNames.Id, value.id);
        StringMember<TProbe>(writer, ProbeNames.Name, value.name);
        StringMember<TProbe>(writer, ProbeNames.Description, value.description);
        StringMember<TProbe>(writer, ProbeNames.Subtitle, value.subtitle);
        StringMember<TProbe>(writer, ProbeNames.Logo, value.logo);

        if (!TProbe.RawEncoderFree || !TryRawEventTail(writer, value))
        {
            if (value.subjectCode is { } subjectCode)
                writer.WriteNumber(ProbeNames.SubjectCode, subjectCode);
            if (value.topicIds is { } topicIds)
            {
                writer.WritePropertyName(ProbeNames.TopicIds);
                Numbers<TProbe>(writer, topicIds);
            }

            if (value.subTopicIds is { } subTopicIds)
            {
                writer.WritePropertyName(ProbeNames.SubTopicIds);
                Numbers<TProbe>(writer, subTopicIds);
            }
        }

        writer.WriteEndObject();
    }

    private static void Performance<TProbe>(Utf8JsonWriter writer, CitmCatalog.Performance value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        if (TProbe.RawNumbers)
        {
            Span<byte> buffer = stackalloc byte[64];
            var block = new RawBlock(buffer);
            block.Bytes(ProbeNames.IdRaw);
            block.Number(value.id);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.EventIdRaw);
            block.Number(value.eventId);
            writer.WriteRawValue(block.Written, skipInputValidation: true);
        }
        else
        {
            writer.WriteNumber(ProbeNames.Id, value.id);
            writer.WriteNumber(ProbeNames.EventId, value.eventId);
        }

        StringMember<TProbe>(writer, ProbeNames.Name, value.name);
        StringMember<TProbe>(writer, ProbeNames.Description, value.description);
        StringMember<TProbe>(writer, ProbeNames.Logo, value.logo);

        if (!TProbe.RawEncoderFree || !TryRawPerformanceMiddle(writer, value))
        {
            if (value.prices is { } prices)
            {
                writer.WritePropertyName(ProbeNames.Prices);
                writer.WriteStartArray();
                foreach (var price in prices)
                {
                    if (price is { } p)
                        Price<TProbe>(writer, p);
                    else
                        writer.WriteNullValue();
                }

                writer.WriteEndArray();
            }

            if (value.seatCategories is { } seatCategories)
            {
                writer.WritePropertyName(ProbeNames.SeatCategories);
                writer.WriteStartArray();
                foreach (var seatCategory in seatCategories)
                {
                    if (seatCategory is { } s)
                        SeatCategory<TProbe>(writer, s);
                    else
                        writer.WriteNullValue();
                }

                writer.WriteEndArray();
            }

            writer.WriteNumber(ProbeNames.Start, value.start);
        }

        StringMember<TProbe>(writer, ProbeNames.SeatMapImage, value.seatMapImage);
        StringMember<TProbe>(writer, ProbeNames.VenueCode, value.venueCode);
        writer.WriteEndObject();
    }

    private static void Price<TProbe>(Utf8JsonWriter writer, CitmCatalog.Price value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        if (TProbe.RawNumbers)
        {
            Span<byte> buffer = stackalloc byte[128];
            var block = new RawBlock(buffer);
            block.Bytes(ProbeNames.AmountRaw);
            block.Number(value.amount);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.AudienceSubCategoryIdRaw);
            block.Number(value.audienceSubCategoryId);
            block.Byte((byte)',');
            block.Bytes(ProbeNames.SeatCategoryIdRaw);
            block.Number(value.seatCategoryId);
            writer.WriteRawValue(block.Written, skipInputValidation: true);
        }
        else
        {
            writer.WriteNumber(ProbeNames.Amount, value.amount);
            writer.WriteNumber(ProbeNames.AudienceSubCategoryId, value.audienceSubCategoryId);
            writer.WriteNumber(ProbeNames.SeatCategoryId, value.seatCategoryId);
        }

        writer.WriteEndObject();
    }

    private static void SeatCategory<TProbe>(Utf8JsonWriter writer, CitmCatalog.SeatCategory value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        writer.WriteNumber(ProbeNames.SeatCategoryId, value.seatCategoryId);
        if (value.areas is { } areas)
        {
            writer.WritePropertyName(ProbeNames.Areas);
            writer.WriteStartArray();
            foreach (var area in areas)
            {
                if (area is { } a)
                    Area<TProbe>(writer, a);
                else
                    writer.WriteNullValue();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void Area<TProbe>(Utf8JsonWriter writer, CitmCatalog.Area value)
        where TProbe : struct, IResponseProbe
    {
        writer.WriteStartObject();
        if (TProbe.RawNumbers)
        {
            Span<byte> buffer = stackalloc byte[512];
            var block = new RawBlock(buffer);
            CitmRaw.AreaMembers(ref block, value);
            if (block.Fits)
            {
                writer.WriteRawValue(block.Written, skipInputValidation: true);
                writer.WriteEndObject();
                return;
            }
        }

        writer.WriteNumber(ProbeNames.AreaId, value.areaId);
        if (value.blockIds is { } blockIds)
        {
            writer.WritePropertyName(ProbeNames.BlockIds);
            Numbers<PlainProbe>(writer, blockIds);
        }

        writer.WriteEndObject();
    }

    private static void Numbers<TProbe>(Utf8JsonWriter writer, int[] value)
        where TProbe : struct, IResponseProbe
    {
        if (TProbe.RawNumbers)
        {
            Span<byte> buffer = stackalloc byte[512];
            var block = new RawBlock(buffer);
            CitmRaw.Numbers(ref block, value);
            if (block.Fits)
            {
                writer.WriteRawValue(block.Written, skipInputValidation: true);
                return;
            }
        }

        writer.WriteStartArray();
        foreach (var number in value)
            writer.WriteNumberValue(number);
        writer.WriteEndArray();
    }

    private static void Key<TProbe>(Utf8JsonWriter writer, int key)
        where TProbe : struct, IResponseProbe
    {
        if (TProbe.ConstantKeys)
            writer.WritePropertyName(ProbeNames.ConstantKey);
        else
            Utf8JsonValues.WritePropertyName(writer, (long)key);
    }

    private static void StringValue<TProbe>(Utf8JsonWriter writer, string? value)
        where TProbe : struct, IResponseProbe
    {
        if (TProbe.ConstantStrings)
            writer.WriteStringValue(ProbeNames.ConstantText);
        else
            writer.WriteStringValue(value);
    }

    private static void StringMember<TProbe>(Utf8JsonWriter writer, JsonEncodedText name, string? value)
        where TProbe : struct, IResponseProbe
    {
        if (value is null)
            return;

        if (TProbe.ConstantStrings)
            writer.WriteString(name, ProbeNames.ConstantText);
        else
            writer.WriteString(name, value);
    }

    private static bool TryRaw(Utf8JsonWriter writer, Dictionary<int, int[]> value)
    {
        var block = new RawBlock(Scratch);
        CitmRaw.IntKeyedNumbers(ref block, value);
        return WriteIfFits(writer, ref block);
    }

    private static bool TryRawEventTail(Utf8JsonWriter writer, CitmCatalog.Event value)
    {
        if (value.subjectCode is null && value.topicIds is null && value.subTopicIds is null)
            return true;

        var block = new RawBlock(Scratch);
        CitmRaw.EventTail(ref block, value);
        return WriteIfFits(writer, ref block);
    }

    private static bool TryRawPerformanceMiddle(Utf8JsonWriter writer, CitmCatalog.Performance value)
    {
        var block = new RawBlock(Scratch);
        CitmRaw.PerformanceMiddle(ref block, value);
        return WriteIfFits(writer, ref block);
    }

    private static bool WriteIfFits(Utf8JsonWriter writer, ref RawBlock block)
    {
        if (!block.Fits)
            return false;

        writer.WriteRawValue(block.Written, skipInputValidation: true);
        return true;
    }
}
