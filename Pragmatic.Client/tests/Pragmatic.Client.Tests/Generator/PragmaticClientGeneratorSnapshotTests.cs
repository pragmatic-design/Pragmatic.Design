using Pragmatic.Testing.Assertions;

namespace Pragmatic.Client.Tests.Generator;

/// <summary>
///     Snapshot tests for PragmaticClientGenerator (Mode A: AdditionalFiles manifest).
///     Verifies: interface, HTTP client, DTOs, enums, errors, DI registration, error mapping.
/// </summary>
public class PragmaticClientGeneratorSnapshotTests : PragmaticClientGeneratorTestBase
{
    private const string SimpleManifest = """
        {
            "$schema": "pragmatic-manifest/v1",
            "version": "1.0.0",
            "assembly": "Showcase.Booking",
            "boundaries": [],
            "endpoints": [
                {
                    "operationId": "Booking.CreateGuest",
                    "httpMethod": "Post",
                    "fullRoute": "/api/v1/guests",
                    "summary": "Create a new guest",
                    "successStatusCode": 201,
                    "isVoid": false,
                    "response": { "type": "global::System.Guid" },
                    "requestBody": {
                        "properties": [
                            { "name": "FirstName", "type": "string", "isRequired": true, "isNullable": false },
                            { "name": "LastName", "type": "string", "isRequired": true, "isNullable": false },
                            { "name": "Email", "type": "string", "isRequired": false, "isNullable": true }
                        ]
                    },
                    "errors": [
                        { "type": "global::Pragmatic.Result.Http.ConflictError", "code": "CONFLICT", "statusCode": 409 }
                    ]
                },
                {
                    "operationId": "Booking.GetGuest",
                    "httpMethod": "Get",
                    "fullRoute": "/api/v1/guests/{id}",
                    "summary": "Get a guest by ID",
                    "successStatusCode": 200,
                    "isVoid": false,
                    "response": { "type": "global::Showcase.Booking.Dtos.GuestDto" },
                    "parameters": [
                        { "name": "id", "in": "path", "type": "global::System.Guid", "isRequired": true }
                    ]
                },
                {
                    "operationId": "Booking.DeleteGuest",
                    "httpMethod": "Delete",
                    "fullRoute": "/api/v1/guests/{id}",
                    "successStatusCode": 204,
                    "isVoid": true,
                    "parameters": [
                        { "name": "id", "in": "path", "type": "global::System.Guid", "isRequired": true }
                    ]
                }
            ],
            "types": [
                {
                    "type": "global::Showcase.Booking.Dtos.GuestDto",
                    "simpleName": "GuestDto",
                    "kind": "dto",
                    "properties": [
                        { "name": "Id", "type": "System.Guid", "isRequired": false, "isNullable": false },
                        { "name": "FirstName", "type": "string", "isRequired": true, "isNullable": false },
                        { "name": "LastName", "type": "string", "isRequired": true, "isNullable": false },
                        { "name": "Email", "type": "string", "isRequired": false, "isNullable": true },
                        { "name": "Status", "type": "GuestStatus", "isRequired": false, "isNullable": false, "isEnum": true }
                    ]
                },
                {
                    "type": "global::Showcase.Booking.Enums.GuestStatus",
                    "simpleName": "GuestStatus",
                    "kind": "enum",
                    "values": ["Active", "Inactive", "Blocked"]
                },
                {
                    "type": "global::Pragmatic.Result.Http.ConflictError",
                    "simpleName": "ConflictError",
                    "kind": "error",
                    "errorCode": "CONFLICT",
                    "errorStatusCode": 409
                }
            ],
            "actions": [],
            "permissions": []
        }
        """;

    [Fact]
    public async Task Manifest_SimpleBooking_GeneratesAllFiles()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var sources = GetGeneratedSources(result);
        await Verify(sources);
    }

    [Fact]
    public void Interface_HasTypedReturnTypes()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var iface = GetGeneratedSource(result, "IBookingClient");

        iface.Should().NotBeNull();
        iface.Should().Contain("Result<System.Guid, Pragmatic.Result.IError>");
        iface.Should().Contain("Result<GuestDto, Pragmatic.Result.IError>");
        iface.Should().Contain("VoidResult<Pragmatic.Result.IError>");
    }

    [Fact]
    public void HttpClient_HasErrorMapping()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var client = GetGeneratedSource(result, "BookingHttpClient");

        client.Should().NotBeNull();
        // No more throw — uses Result.Failure
        client.Should().NotContain("throw new PragmaticClientException");
        client.Should().Contain("MapErrorAsync");
        client.Should().Contain(".Failure(await MapErrorAsync");
        client.Should().Contain("\"CONFLICT\" => new ConflictError(),");
    }

    [Fact]
    public void ResponseDtos_GeneratedWithProperties()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var dto = GetGeneratedSource(result, "GuestDto.g.cs");

        dto.Should().NotBeNull();
        dto.Should().Contain("public sealed record GuestDto");
        dto.Should().Contain("public System.Guid Id { get; init; }");
        dto.Should().Contain("public string FirstName { get; init; }");
        dto.Should().Contain("public string? Email { get; init; }");
    }

    [Fact]
    public void Enums_GeneratedWithValues()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var enumSource = GetGeneratedSource(result, "GuestStatus.g.cs");

        enumSource.Should().NotBeNull();
        enumSource.Should().Contain("public enum GuestStatus");
        enumSource.Should().Contain("Active");
        enumSource.Should().Contain("Inactive");
        enumSource.Should().Contain("Blocked");
    }

    [Fact]
    public void Errors_ImplementIError()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var error = GetGeneratedSource(result, "ConflictError.g.cs");

        error.Should().NotBeNull();
        error.Should().Contain("Pragmatic.Result.IError");
        error.Should().Contain("\"CONFLICT\"");
        error.Should().Contain("409");
    }

    [Fact]
    public void Registration_GeneratesExtensionMethod()
    {
        var result = RunGeneratorWithManifest(SimpleManifest);
        var reg = GetGeneratedSource(result, "BookingClientExtensions");

        reg.Should().NotBeNull();
        reg.Should().Contain("AddBookingClient");
        reg.Should().Contain("IBookingClient, BookingHttpClient");
    }

    [Fact]
    public void EmptyManifest_GeneratesNothing()
    {
        var result = RunGeneratorWithManifest("""{ "assembly": "Empty", "endpoints": [] }""");
        var sources = GetGeneratedSources(result);
        sources.Should().BeEmpty();
    }
}
