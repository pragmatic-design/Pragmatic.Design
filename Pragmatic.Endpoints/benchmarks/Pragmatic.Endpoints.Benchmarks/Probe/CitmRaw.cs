using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>The parts of <c>citm_catalog.json</c> the encoder cannot change, formatted as the writer writes them.</summary>
internal static class CitmRaw
{
    /// <summary><c>[1,2,3]</c>.</summary>
    public static void Numbers(ref RawBlock block, int[] value)
    {
        block.Byte((byte)'[');
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0)
                block.Byte((byte)',');
            block.Number(value[i]);
        }

        block.Byte((byte)']');
    }

    /// <summary><c>"areaId":1,"blockIds":[…]</c>, without the braces.</summary>
    public static void AreaMembers(ref RawBlock block, CitmCatalog.Area value)
    {
        block.Bytes(ProbeNames.AreaIdRaw);
        block.Number(value.areaId);
        if (value.blockIds is { } blockIds)
        {
            block.Byte((byte)',');
            block.Bytes(ProbeNames.BlockIdsRaw);
            Numbers(ref block, blockIds);
        }
    }

    /// <summary><c>{"1":[…],"2":null}</c>.</summary>
    public static void IntKeyedNumbers(ref RawBlock block, Dictionary<int, int[]> value)
    {
        block.Byte((byte)'{');
        var first = true;
        foreach (var entry in value)
        {
            if (!first)
                block.Byte((byte)',');
            first = false;

            block.Byte((byte)'"');
            block.Number(entry.Key);
            block.Byte((byte)'"');
            block.Byte((byte)':');
            if (entry.Value is { } ids)
                Numbers(ref block, ids);
            else
                block.Bytes("null"u8);
        }

        block.Byte((byte)'}');
    }

    /// <summary>An event's <c>subjectCode</c>, <c>topicIds</c> and <c>subTopicIds</c>, the ones it has.</summary>
    public static void EventTail(ref RawBlock block, CitmCatalog.Event value)
    {
        var first = true;
        if (value.subjectCode is { } subjectCode)
        {
            block.Bytes(ProbeNames.SubjectCodeRaw);
            block.Number(subjectCode);
            first = false;
        }

        if (value.topicIds is { } topicIds)
        {
            if (!first)
                block.Byte((byte)',');
            first = false;
            block.Bytes(ProbeNames.TopicIdsRaw);
            Numbers(ref block, topicIds);
        }

        if (value.subTopicIds is { } subTopicIds)
        {
            if (!first)
                block.Byte((byte)',');
            block.Bytes(ProbeNames.SubTopicIdsRaw);
            Numbers(ref block, subTopicIds);
        }
    }

    /// <summary>A performance's <c>prices</c>, <c>seatCategories</c> and <c>start</c>.</summary>
    public static void PerformanceMiddle(ref RawBlock block, CitmCatalog.Performance value)
    {
        if (value.prices is { } prices)
        {
            block.Bytes(ProbeNames.PricesRaw);
            block.Byte((byte)'[');
            for (var i = 0; i < prices.Length; i++)
            {
                if (i > 0)
                    block.Byte((byte)',');
                if (prices[i] is { } price)
                    Price(ref block, price);
                else
                    block.Bytes("null"u8);
            }

            block.Byte((byte)']');
            block.Byte((byte)',');
        }

        if (value.seatCategories is { } seatCategories)
        {
            block.Bytes(ProbeNames.SeatCategoriesRaw);
            block.Byte((byte)'[');
            for (var i = 0; i < seatCategories.Length; i++)
            {
                if (i > 0)
                    block.Byte((byte)',');
                if (seatCategories[i] is { } seatCategory)
                    SeatCategory(ref block, seatCategory);
                else
                    block.Bytes("null"u8);
            }

            block.Byte((byte)']');
            block.Byte((byte)',');
        }

        block.Bytes(ProbeNames.StartRaw);
        block.Number(value.start);
    }

    private static void Price(ref RawBlock block, CitmCatalog.Price value)
    {
        block.Byte((byte)'{');
        block.Bytes(ProbeNames.AmountRaw);
        block.Number(value.amount);
        block.Byte((byte)',');
        block.Bytes(ProbeNames.AudienceSubCategoryIdRaw);
        block.Number(value.audienceSubCategoryId);
        block.Byte((byte)',');
        block.Bytes(ProbeNames.SeatCategoryIdRaw);
        block.Number(value.seatCategoryId);
        block.Byte((byte)'}');
    }

    private static void SeatCategory(ref RawBlock block, CitmCatalog.SeatCategory value)
    {
        block.Byte((byte)'{');
        block.Bytes(ProbeNames.SeatCategoryIdRaw);
        block.Number(value.seatCategoryId);
        if (value.areas is { } areas)
        {
            block.Byte((byte)',');
            block.Bytes(ProbeNames.AreasRaw);
            block.Byte((byte)'[');
            for (var i = 0; i < areas.Length; i++)
            {
                if (i > 0)
                    block.Byte((byte)',');
                if (areas[i] is { } area)
                {
                    block.Byte((byte)'{');
                    AreaMembers(ref block, area);
                    block.Byte((byte)'}');
                }
                else
                {
                    block.Bytes("null"u8);
                }
            }

            block.Byte((byte)']');
        }

        block.Byte((byte)'}');
    }
}
