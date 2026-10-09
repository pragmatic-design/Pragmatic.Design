using System.Text.Json;
using Pragmatic.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     The names the probe writers write, encoded once as the generated writer encodes them, and as raw bytes
///     (<c>"name":</c>) for the runs written as one raw value.
/// </summary>
internal static class ProbeNames
{
    public static readonly JsonEncodedText AreaNames = GeneratedJsonDefaults.Encode("areaNames");
    public static readonly JsonEncodedText AudienceSubCategoryNames = GeneratedJsonDefaults.Encode("audienceSubCategoryNames");
    public static readonly JsonEncodedText BlockNames = GeneratedJsonDefaults.Encode("blockNames");
    public static readonly JsonEncodedText SeatCategoryNames = GeneratedJsonDefaults.Encode("seatCategoryNames");
    public static readonly JsonEncodedText SubTopicNames = GeneratedJsonDefaults.Encode("subTopicNames");
    public static readonly JsonEncodedText SubjectNames = GeneratedJsonDefaults.Encode("subjectNames");
    public static readonly JsonEncodedText TopicNames = GeneratedJsonDefaults.Encode("topicNames");
    public static readonly JsonEncodedText TopicSubTopics = GeneratedJsonDefaults.Encode("topicSubTopics");
    public static readonly JsonEncodedText VenueNames = GeneratedJsonDefaults.Encode("venueNames");
    public static readonly JsonEncodedText Events = GeneratedJsonDefaults.Encode("events");
    public static readonly JsonEncodedText Performances = GeneratedJsonDefaults.Encode("performances");
    public static readonly JsonEncodedText Id = GeneratedJsonDefaults.Encode("id");
    public static readonly JsonEncodedText Name = GeneratedJsonDefaults.Encode("name");
    public static readonly JsonEncodedText Description = GeneratedJsonDefaults.Encode("description");
    public static readonly JsonEncodedText Subtitle = GeneratedJsonDefaults.Encode("subtitle");
    public static readonly JsonEncodedText Logo = GeneratedJsonDefaults.Encode("logo");
    public static readonly JsonEncodedText SubjectCode = GeneratedJsonDefaults.Encode("subjectCode");
    public static readonly JsonEncodedText TopicIds = GeneratedJsonDefaults.Encode("topicIds");
    public static readonly JsonEncodedText SubTopicIds = GeneratedJsonDefaults.Encode("subTopicIds");
    public static readonly JsonEncodedText EventId = GeneratedJsonDefaults.Encode("eventId");
    public static readonly JsonEncodedText Prices = GeneratedJsonDefaults.Encode("prices");
    public static readonly JsonEncodedText SeatCategories = GeneratedJsonDefaults.Encode("seatCategories");
    public static readonly JsonEncodedText Start = GeneratedJsonDefaults.Encode("start");
    public static readonly JsonEncodedText SeatMapImage = GeneratedJsonDefaults.Encode("seatMapImage");
    public static readonly JsonEncodedText VenueCode = GeneratedJsonDefaults.Encode("venueCode");
    public static readonly JsonEncodedText Amount = GeneratedJsonDefaults.Encode("amount");
    public static readonly JsonEncodedText AudienceSubCategoryId = GeneratedJsonDefaults.Encode("audienceSubCategoryId");
    public static readonly JsonEncodedText SeatCategoryId = GeneratedJsonDefaults.Encode("seatCategoryId");
    public static readonly JsonEncodedText Areas = GeneratedJsonDefaults.Encode("areas");
    public static readonly JsonEncodedText AreaId = GeneratedJsonDefaults.Encode("areaId");
    public static readonly JsonEncodedText BlockIds = GeneratedJsonDefaults.Encode("blockIds");

    public static readonly JsonEncodedText Items = GeneratedJsonDefaults.Encode("items");
    public static readonly JsonEncodedText Page = GeneratedJsonDefaults.Encode("page");
    public static readonly JsonEncodedText PageSize = GeneratedJsonDefaults.Encode("pageSize");
    public static readonly JsonEncodedText TotalCount = GeneratedJsonDefaults.Encode("totalCount");
    public static readonly JsonEncodedText GuestId = GeneratedJsonDefaults.Encode("guestId");
    public static readonly JsonEncodedText PropertyId = GeneratedJsonDefaults.Encode("propertyId");
    public static readonly JsonEncodedText CheckIn = GeneratedJsonDefaults.Encode("checkIn");
    public static readonly JsonEncodedText CheckOut = GeneratedJsonDefaults.Encode("checkOut");
    public static readonly JsonEncodedText ExpectedArrival = GeneratedJsonDefaults.Encode("expectedArrival");
    public static readonly JsonEncodedText ActualArrival = GeneratedJsonDefaults.Encode("actualArrival");
    public static readonly JsonEncodedText NumberOfGuests = GeneratedJsonDefaults.Encode("numberOfGuests");
    public static readonly JsonEncodedText TotalAmount = GeneratedJsonDefaults.Encode("totalAmount");
    public static readonly JsonEncodedText Currency = GeneratedJsonDefaults.Encode("currency");
    public static readonly JsonEncodedText PropertyName = GeneratedJsonDefaults.Encode("propertyName");
    public static readonly JsonEncodedText GuestFirstName = GeneratedJsonDefaults.Encode("guestFirstName");
    public static readonly JsonEncodedText GuestLastName = GeneratedJsonDefaults.Encode("guestLastName");

    /// <summary>A constant key and a constant text, for the subtraction rows.</summary>
    public static readonly JsonEncodedText ConstantKey = GeneratedJsonDefaults.Encode("0");
    public static readonly JsonEncodedText ConstantText = GeneratedJsonDefaults.Encode("");

    public static readonly byte[] IdRaw = Raw(Id);
    public static readonly byte[] SubjectCodeRaw = Raw(SubjectCode);
    public static readonly byte[] TopicIdsRaw = Raw(TopicIds);
    public static readonly byte[] SubTopicIdsRaw = Raw(SubTopicIds);
    public static readonly byte[] EventIdRaw = Raw(EventId);
    public static readonly byte[] StartRaw = Raw(Start);
    public static readonly byte[] AmountRaw = Raw(Amount);
    public static readonly byte[] AudienceSubCategoryIdRaw = Raw(AudienceSubCategoryId);
    public static readonly byte[] SeatCategoryIdRaw = Raw(SeatCategoryId);
    public static readonly byte[] AreasRaw = Raw(Areas);
    public static readonly byte[] AreaIdRaw = Raw(AreaId);
    public static readonly byte[] BlockIdsRaw = Raw(BlockIds);
    public static readonly byte[] PricesRaw = Raw(Prices);
    public static readonly byte[] SeatCategoriesRaw = Raw(SeatCategories);

    public static readonly byte[] PageRaw = Raw(Page);
    public static readonly byte[] PageSizeRaw = Raw(PageSize);
    public static readonly byte[] TotalCountRaw = Raw(TotalCount);
    public static readonly byte[] GuestIdRaw = Raw(GuestId);
    public static readonly byte[] PropertyIdRaw = Raw(PropertyId);
    public static readonly byte[] CheckInRaw = Raw(CheckIn);
    public static readonly byte[] CheckOutRaw = Raw(CheckOut);
    public static readonly byte[] ExpectedArrivalRaw = Raw(ExpectedArrival);
    public static readonly byte[] ActualArrivalRaw = Raw(ActualArrival);
    public static readonly byte[] NumberOfGuestsRaw = Raw(NumberOfGuests);
    public static readonly byte[] TotalAmountRaw = Raw(TotalAmount);

    private static byte[] Raw(JsonEncodedText name)
    {
        var encoded = name.EncodedUtf8Bytes;
        var raw = new byte[encoded.Length + 3];
        raw[0] = (byte)'"';
        encoded.CopyTo(raw.AsSpan(1));
        raw[^2] = (byte)'"';
        raw[^1] = (byte)':';
        return raw;
    }
}
