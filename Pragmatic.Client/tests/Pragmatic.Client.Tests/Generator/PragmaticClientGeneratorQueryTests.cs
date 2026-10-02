using Pragmatic.Testing.Assertions;

namespace Pragmatic.Client.Tests.Generator;

/// <summary>
///     Three behaviours the generated C# client needs against a real server, all taken from shapes
///     present in the Showcase manifest: query parameters are sent, a GET carrying a query object stays
///     a GET (as a POST it answers 405), and path parameters are URL-encoded.
///     The TypeScript emitter (<c>TsEmitter</c>) does all three — these tests pin the C# side to it.
/// </summary>
public class PragmaticClientGeneratorQueryTests : PragmaticClientGeneratorTestBase
{
    private const string QueryManifest = """
        {
            "$schema": "pragmatic-manifest/v1",
            "assembly": "Showcase.Booking",
            "endpoints": [
                {
                    "operationId": "Booking.SearchAvailableRooms",
                    "httpMethod": "Get",
                    "fullRoute": "/api/availability",
                    "isVoid": false,
                    "response": { "type": "global::System.Guid" },
                    "parameters": [
                        { "name": "propertyId", "in": "query", "type": "global::System.Guid", "isRequired": false },
                        { "name": "checkIn", "in": "query", "type": "global::System.DateTimeOffset", "isRequired": false },
                        { "name": "guests", "in": "query", "type": "int", "isRequired": false },
                        { "name": "sort", "in": "query", "type": "global::Showcase.Booking.Enums.SortDirection?", "isRequired": false }
                    ]
                },
                {
                    "operationId": "Booking.SearchGuests",
                    "httpMethod": "Get",
                    "fullRoute": "/api/guests/search",
                    "isVoid": false,
                    "response": { "type": "global::System.Guid" },
                    "parameters": [
                        { "name": "email", "in": "query", "type": "string", "isRequired": false }
                    ],
                    "requestBody": {
                        "properties": [
                            { "name": "Page", "type": "int", "isRequired": false, "isNullable": false },
                            { "name": "PageSize", "type": "int", "isRequired": false, "isNullable": false }
                        ]
                    }
                },
                {
                    "operationId": "Booking.GetGuestNote",
                    "httpMethod": "Get",
                    "fullRoute": "/api/guests/{guestId}/notes/{noteKey}",
                    "isVoid": false,
                    "response": { "type": "global::System.Guid" },
                    "parameters": [
                        { "name": "guestId", "in": "path", "type": "global::System.Guid", "isRequired": true },
                        { "name": "noteKey", "in": "path", "type": "string", "isRequired": true }
                    ]
                }
            ],
            "types": [
                {
                    "type": "global::Showcase.Booking.Enums.SortDirection",
                    "simpleName": "SortDirection",
                    "kind": "enum",
                    "values": ["Ascending", "Descending"]
                }
            ]
        }
        """;

    private static string Client() =>
        GetGeneratedSource(RunGeneratorWithManifest(QueryManifest), "BookingHttpClient")!;

    private static string Interface() =>
        GetGeneratedSource(RunGeneratorWithManifest(QueryManifest), "IBookingClient")!;

    [Fact]
    public void QueryParameters_AppearInTheSignature_AsOptionalArguments()
    {
        var iface = Interface();

        iface.Should().Contain(
            "SearchAvailableRooms(System.Guid? propertyId = null, System.DateTimeOffset? checkIn = null, " +
            "int? guests = null, SortDirection? sort = null, CancellationToken ct = default)");
    }

    [Fact]
    public void QueryParameters_AreAppendedToTheUrl_WhenSupplied()
    {
        var client = Client();

        client.Should().Contain("var query = new List<string>();");
        client.Should().Contain("if (propertyId is not null)");
        client.Should().Contain("query.Add(\"propertyId=\" + System.Uri.EscapeDataString(FormatQueryValue(propertyId)));");
        client.Should().Contain("if (query.Count > 0)");
        client.Should().Contain("url += \"?\" + string.Join(\"&\", query);");
    }

    [Fact]
    public void QueryValues_AreFormattedCultureInvariantly()
    {
        var client = Client();

        client.Should().Contain("private static string FormatQueryValue(object value)");
        client.Should().Contain("dto.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture)");
    }

    [Fact]
    public void EnumQueryParameter_IsTyped_NotObject()
    {
        Interface().Should().Contain("SortDirection? sort = null")
            .And.NotContain("object? sort");
    }

    [Fact]
    public void GetWithQueryObject_StaysAGet_AndDoesNotPostABody()
    {
        var client = Client();

        // The server exposes GET /api/guests/search; issuing a POST answered 405 on every call.
        client.Should().Contain("_http.GetAsync(url, ct)");
        client.Should().NotContain("PostAsJsonAsync(\"/api/guests/search\"");
        client.Should().NotContain("PostAsJsonAsync(url, request, ct)");
    }

    [Fact]
    public void GetWithQueryObject_SendsBodyPropertiesAsQueryString()
    {
        var client = Client();

        client.Should().Contain("query.Add(\"Page=\"");
        client.Should().Contain("query.Add(\"PageSize=\"");
    }

    [Fact]
    public void GetWithQueryObject_GeneratesNoRequestDto()
    {
        var sources = GetGeneratedSources(RunGeneratorWithManifest(QueryManifest));

        sources.Keys.Should().NotContain(k => k.Contains("SearchGuestsRequest"));
    }

    [Fact]
    public void PathParameters_AreUrlEncoded()
    {
        var client = Client();

        client.Should().Contain(
            "var url = $\"/api/guests/{System.Uri.EscapeDataString(guestId.ToString()!)}" +
            "/notes/{System.Uri.EscapeDataString(noteKey.ToString()!)}\";");
    }

    /// <summary>
    ///     An array response is resolved through its element type. The whole name — <c>GuestDto[]</c> — used
    ///     to be looked up in the manifest's type table, never matched, and a well-described DTO came back as
    ///     <c>object</c>: the endpoint lost its type for no reason at all.
    /// </summary>
    [Fact]
    public void ArrayResponseType_IsResolvedThroughItsElementType()
    {
        const string manifest = """
            {
                "assembly": "Showcase.Booking",
                "endpoints": [
                    {
                        "operationId": "Booking.ListRooms",
                        "httpMethod": "Get",
                        "fullRoute": "/api/rooms",
                        "isVoid": false,
                        "response": { "type": "global::Showcase.Booking.Dtos.AvailableRoomResult[]" }
                    }
                ],
                "types": [
                    {
                        "type": "global::Showcase.Booking.Dtos.AvailableRoomResult",
                        "simpleName": "AvailableRoomResult",
                        "kind": "dto",
                        "properties": [
                            { "name": "RoomTypeName", "type": "string", "isRequired": true, "isNullable": false }
                        ]
                    }
                ]
            }
            """;

        var result = RunGeneratorWithManifest(manifest);

        GetGeneratedSource(result, "IBookingClient").Should()
            .Contain("Result<AvailableRoomResultDto[], Pragmatic.Result.IError>")
            .And.NotContain("Result<object,");
    }

    [Fact]
    public void EndpointWithoutQueryValues_DoesNotBuildAQueryString()
    {
        var client = Client();
        var getNote = client[client.IndexOf("GetGuestNote(", StringComparison.Ordinal)..];
        var body = getNote[..getNote.IndexOf("    }", StringComparison.Ordinal)];

        body.Should().NotContain("var query = new List<string>();");
    }
}
