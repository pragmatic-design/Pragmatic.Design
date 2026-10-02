using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Manifest.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     <c>OpenApiJsonGenerator</c> turns the module manifests into an OpenAPI 3.1 document at compile
///     time. It parses JSON produced by another part of the generator and re-emits it raw in places, so
///     a mapping mistake produces a document that is either wrong or not JSON at all — which these
///     tests exist to catch.
/// </summary>
public class OpenApiJsonGeneratorTests
{
    /// <summary>Minimal but realistic module manifest, shaped exactly like ManifestJsonTemplate's output.</summary>
    private static string Manifest(string endpoints, string types = "[]") => $$"""
        {
          "$schema": "pragmatic-manifest/v1",
          "version": "1.0.0",
          "assembly": "Showcase.Booking",
          "boundaries": [],
          "endpoints": {{endpoints}},
          "types": {{types}},
          "actions": [],
          "permissions": []
        }
        """;

    private static JsonDocument Generate(params string[] manifests)
    {
        var json = OpenApiJsonGenerator.Generate("Showcase", manifests).Json;
        json.Should().NotBeNull();
        JsonValidator.IsValid(json).Should().BeTrue("the OpenAPI document is embedded and served as JSON");
        return JsonDocument.Parse(json!);
    }

    [Fact]
    public void Generate_NoManifestsOrNoEndpoints_ReturnsNull()
    {
        OpenApiJsonGenerator.Generate("Showcase", []).Json.Should().BeNull();
        OpenApiJsonGenerator.Generate("Showcase", [Manifest("[]")]).Json.Should().BeNull();
    }

    [Fact]
    public void Generate_MalformedManifest_IsSkippedWithoutLosingTheOthers()
    {
        using var doc = Generate("{ not json", Manifest("""
            [{ "operationId": "Booking.GetReservation", "httpMethod": "GET", "fullRoute": "/reservations/{id}",
               "successStatusCode": 200, "isVoid": false }]
            """));

        doc.RootElement.GetProperty("paths").GetProperty("/reservations/{id}").TryGetProperty("get", out _)
            .Should().BeTrue();
    }

    [Fact]
    public void Generate_Operation_CarriesIdTagsParametersAndPermissions()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Booking.GetReservation",
              "httpMethod": "GET",
              "fullRoute": "/reservations/{id}",
              "summary": "Reads a reservation",
              "successStatusCode": 200,
              "isVoid": false,
              "parameters": [
                { "name": "id", "in": "path", "type": "System.Guid", "isRequired": true },
                { "name": "page", "in": "query", "type": "int", "isRequired": false, "defaultValue": "1" }
              ],
              "authorization": { "allowAnonymous": false, "requiredPermissions": ["booking.read"] }
            }]
            """));

        var op = doc.RootElement.GetProperty("paths").GetProperty("/reservations/{id}").GetProperty("get");
        op.GetProperty("operationId").GetString().Should().Be("Booking.GetReservation");
        op.GetProperty("summary").GetString().Should().Be("Reads a reservation");
        op.GetProperty("tags")[0].GetString().Should().Be("Booking");

        var parameters = op.GetProperty("parameters");
        parameters[0].GetProperty("in").GetString().Should().Be("path");
        parameters[0].GetProperty("schema").GetProperty("format").GetString().Should().Be("uuid");
        parameters[1].GetProperty("schema").GetProperty("type").GetString().Should().Be("integer");
        parameters[1].GetProperty("schema").GetProperty("default").GetInt32().Should().Be(1);

        op.GetProperty("x-pragmatic-permissions")[0].GetString().Should().Be("booking.read");
        doc.RootElement.GetProperty("openapi").GetString().Should().Be("3.1.0");
    }

    /// <summary>
    ///     A <c>[DefaultValue]</c> whose text parses as a decimal but is not a JSON number ("1,234" —
    ///     group separator) must not be emitted raw: <c>"default": 1,234</c> is an extra value inside the
    ///     schema object, i.e. a corrupt document. It falls back to a JSON string.
    /// </summary>
    [Theory]
    [InlineData("1,234")]
    [InlineData("1.2.3")]
    [InlineData("+7")]
    [InlineData("5-")]
    public void Generate_NumberDefaultThatIsNotAJsonNumber_IsEmittedAsAString(string defaultValue)
    {
        using var doc = Generate(Manifest($$"""
            [{
              "operationId": "Booking.List", "httpMethod": "GET", "fullRoute": "/list",
              "successStatusCode": 200, "isVoid": false,
              "parameters": [{ "name": "amount", "in": "query", "type": "decimal", "isRequired": false,
                               "defaultValue": {{JsonSerializer.Serialize(defaultValue)}} }]
            }]
            """));

        var schema = doc.RootElement.GetProperty("paths").GetProperty("/list").GetProperty("get")
            .GetProperty("parameters")[0].GetProperty("schema");

        schema.GetProperty("type").GetString().Should().Be("number");
        schema.GetProperty("default").ValueKind.Should().Be(JsonValueKind.String);
        schema.GetProperty("default").GetString().Should().Be(defaultValue);
    }

    [Fact]
    public void Generate_WellFormedNumericAndBooleanDefaults_KeepTheirJsonType()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Booking.List", "httpMethod": "GET", "fullRoute": "/list",
              "successStatusCode": 200, "isVoid": false,
              "parameters": [
                { "name": "amount", "in": "query", "type": "decimal", "isRequired": false, "defaultValue": "1.5" },
                { "name": "size", "in": "query", "type": "int", "isRequired": false, "defaultValue": "20" },
                { "name": "flag", "in": "query", "type": "bool", "isRequired": false, "defaultValue": "true" }
              ]
            }]
            """));

        var parameters = doc.RootElement.GetProperty("paths").GetProperty("/list").GetProperty("get")
            .GetProperty("parameters");

        parameters[0].GetProperty("schema").GetProperty("default").GetDecimal().Should().Be(1.5m);
        parameters[1].GetProperty("schema").GetProperty("default").GetInt32().Should().Be(20);
        parameters[2].GetProperty("schema").GetProperty("default").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Generate_RequestBody_GetsItsOwnSchemaWithRequiredProperties()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Booking.CreateReservation", "httpMethod": "POST", "fullRoute": "/reservations",
              "successStatusCode": 201, "isVoid": false,
              "requestBody": { "properties": [
                { "name": "GuestId", "type": "System.Guid", "isRequired": true, "isNullable": false },
                { "name": "Notes", "type": "string", "isRequired": false, "isNullable": true, "maxLength": 200 }
              ]}
            }]
            """));

        var op = doc.RootElement.GetProperty("paths").GetProperty("/reservations").GetProperty("post");
        op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/CreateReservationRequest");

        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("CreateReservationRequest");
        schema.GetProperty("properties").GetProperty("guestId").GetProperty("format").GetString().Should().Be("uuid");
        // Was "nullable": true — a 3.0 keyword in a document that declares 3.1, so every reader dropped
        // it and the property read as never-null. 3.1 says the same thing as a union of types.
        Types(schema.GetProperty("properties").GetProperty("notes"))
            .Should().Equal("string", "null");
        schema.GetProperty("properties").GetProperty("notes").GetProperty("maxLength").GetInt32().Should().Be(200);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Equal("guestId");
    }

    /// <summary>
    ///     Every constraint the manifest carries lands on the schema under its JSON Schema keyword —
    ///     on a request body and on a described type alike.
    /// </summary>
    /// <remarks>
    ///     <c>maxLength</c> was read here from the start; the other eight keywords were not, so the
    ///     document published a contract looser than the validator behind it. Numbers stay numbers:
    ///     a <c>minimum</c> rendered as a string is a keyword every reader ignores.
    /// </remarks>
    [Fact]
    public void Generate_Constraints_LandOnTheSchemaUnderTheirKeywords()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Booking.CreateReservation", "httpMethod": "POST", "fullRoute": "/reservations",
              "successStatusCode": 201, "isVoid": false,
              "response": { "type": "Showcase.Booking.ReservationDto" },
              "requestBody": { "properties": [
                { "name": "Name", "type": "string", "isRequired": true, "isNullable": false,
                  "minLength": 3, "maxLength": 60, "pattern": "^[A-Z]", "format": "email" },
                { "name": "Guests", "type": "int", "isRequired": true, "isNullable": false,
                  "minimum": 1, "maximum": 10 },
                { "name": "Deposit", "type": "decimal", "isRequired": true, "isNullable": false,
                  "exclusiveMinimum": 0, "exclusiveMaximum": 99.5 },
                { "name": "Tags", "type": "List<string>", "isRequired": false, "isNullable": true,
                  "minItems": 1, "maxItems": 5 }
              ]}
            }]
            """, """
            [{ "type": "Showcase.Booking.ReservationDto", "simpleName": "ReservationDto", "kind": "dto",
               "properties": [
                 { "name": "Code", "type": "string", "isRequired": true, "isNullable": false, "minLength": 2, "pattern": "^R" }
               ] }]
            """));

        var request = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("CreateReservationRequest").GetProperty("properties");

        var name = request.GetProperty("name");
        name.GetProperty("minLength").GetInt32().Should().Be(3);
        name.GetProperty("maxLength").GetInt32().Should().Be(60);
        name.GetProperty("pattern").GetString().Should().Be("^[A-Z]");
        name.GetProperty("format").GetString().Should().Be("email");

        var guests = request.GetProperty("guests");
        guests.GetProperty("minimum").GetDouble().Should().Be(1);
        guests.GetProperty("maximum").GetDouble().Should().Be(10);

        var deposit = request.GetProperty("deposit");
        deposit.GetProperty("exclusiveMinimum").GetDouble().Should().Be(0);
        deposit.GetProperty("exclusiveMaximum").GetDouble().Should().Be(99.5);

        var tags = request.GetProperty("tags");
        tags.GetProperty("minItems").GetInt32().Should().Be(1);
        tags.GetProperty("maxItems").GetInt32().Should().Be(5);

        var code = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ReservationDto").GetProperty("properties").GetProperty("code");
        code.GetProperty("minLength").GetInt32().Should().Be(2);
        code.GetProperty("pattern").GetString().Should().Be("^R");
    }

    /// <summary>
    ///     A property renamed on the wire is renamed in the contract too.
    /// </summary>
    /// <remarks>
    ///     The document and the request body DTO are generated by different code from the same
    ///     declaration, and only the DTO was reading <c>[JsonPropertyName]</c>. So an import whose
    ///     property is <c>Rows</c> and whose wire name is <c>people</c> accepted <c>people</c> and
    ///     published <c>rows</c> — and a client generated from the published contract posted a field
    ///     its own server would reject. Found in an application, not here.
    /// </remarks>
    [Fact]
    public void Generate_RequestBodyPropertyWithAWireName_PublishesTheWireName()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Workspaces.ImportMembers", "httpMethod": "POST", "fullRoute": "/members/import",
              "successStatusCode": 200, "isVoid": false,
              "requestBody": { "properties": [
                { "name": "WorkspaceId", "type": "System.Guid", "isRequired": true, "isNullable": false },
                { "name": "Rows", "wireName": "people", "type": "string", "isRequired": true, "isNullable": false }
              ]}
            }]
            """));

        var properties = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ImportMembersRequest").GetProperty("properties");

        properties.TryGetProperty("people", out _).Should().BeTrue(
            "the contract must ask for what the endpoint accepts");
        properties.TryGetProperty("rows", out _).Should().BeFalse(
            "the property name is ours and does not belong in the contract");

        doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ImportMembersRequest").GetProperty("required")
            .EnumerateArray().Select(e => e.GetString())
            .Should().Contain("people", "a required property is required under the name it is sent by");
    }

    /// <summary>
    ///     Anything that is not one of seven primitives is published with its declared shape, not as a
    ///     string.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>MapJsonSchemaType</c> maps seven primitive names and closes with <c>_ =&gt; "string"</c>,
    ///         so a property renderer that relies on it alone turns a list, a dictionary, a nested object
    ///         and an enum into <c>"type": "string"</c>. On the reference application that is
    ///         <b>43 properties whose declared type would be a lie</b>, and <b>21 of the 22 types involved
    ///         already have a schema in the same document</b>. The catalogue has the answer, so the
    ///         renderers ask it first.
    ///     </para>
    ///     <para>
    ///         Not cosmetic: a client generated from that document sends a string where an array is
    ///         required, and the application it was generated from works — so the mismatch surfaces at
    ///         the consumer, once, in production.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Generate_ACollectionProperty_IsAnArrayOfItsItemType()
    {
        var properties = RequestProperties(Generate(Manifest("""
            [{
              "operationId": "Catalog.CreateAmenity", "httpMethod": "POST", "fullRoute": "/amenities",
              "successStatusCode": 201, "isVoid": false,
              "requestBody": { "properties": [
                { "name": "Keywords", "type": "System.Collections.Generic.List<string>", "isRequired": true, "isNullable": false },
                { "name": "Scores", "type": "int[]", "isRequired": false, "isNullable": true }
              ]}
            }]
            """)), "CreateAmenityRequest");

        var keywords = properties.GetProperty("keywords");
        Types(keywords).Should().Equal("array");
        keywords.GetProperty("items").GetProperty("type").GetString().Should().Be("string");

        var scores = properties.GetProperty("scores");
        // The list may be absent; its items may not be — the union is on the property, not on the item.
        Types(scores).Should().Equal("array", "null");
        scores.GetProperty("items").GetProperty("type").GetString().Should().Be("integer");
    }

    /// <remarks>
    ///     The case that pays for the whole change: the item type is a schema this document already
    ///     emits, so the array can point at it instead of describing nothing.
    /// </remarks>
    [Fact]
    public void Generate_ACollectionOfAKnownType_RefsThatTypesSchema()
    {
        var properties = RequestProperties(Generate(Manifest("""
            [{
              "operationId": "Catalog.ImportRates", "httpMethod": "POST", "fullRoute": "/rates/import",
              "successStatusCode": 200, "isVoid": false,
              "requestBody": { "properties": [
                { "name": "Rates", "type": "Showcase.Catalog.Dtos.RateRow[]", "isRequired": true, "isNullable": false }
              ]}
            }]
            """,
            """
            [{ "type": "Showcase.Catalog.Dtos.RateRow", "simpleName": "RateRow", "kind": "dto",
               "properties": [{ "name": "Amount", "type": "decimal", "isRequired": true, "isNullable": false }] }]
            """)), "ImportRatesRequest");

        var rates = properties.GetProperty("rates");
        rates.GetProperty("type").GetString().Should().Be("array");
        rates.GetProperty("items").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/RateRow");
    }

    /// <remarks>
    ///     A nested object is the commonest of the 43: a DTO holding another DTO, an entity holding its
    ///     navigation, an action carrying a request record. Every one of them said "string".
    /// </remarks>
    [Fact]
    public void Generate_ANestedKnownType_IsARef()
    {
        var doc = Generate(Manifest("""
            [{
              "operationId": "Booking.CreateReservation", "httpMethod": "POST", "fullRoute": "/reservations",
              "successStatusCode": 201, "isVoid": false,
              "requestBody": { "properties": [
                { "name": "Request", "type": "Showcase.Booking.Dtos.ReservationRequest", "isRequired": true, "isNullable": false }
              ]}
            }]
            """,
            """
            [{ "type": "Showcase.Booking.Dtos.ReservationRequest", "simpleName": "ReservationRequest", "kind": "dto",
               "properties": [{ "name": "GuestId", "type": "System.Guid", "isRequired": true, "isNullable": false }] }]
            """));

        RequestProperties(doc, "CreateReservationRequest").GetProperty("request")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/ReservationRequest");
    }

    /// <remarks>
    ///     A dictionary is an object keyed by whatever the key type serialises to — the value type is
    ///     the part a schema can pin down, and <c>additionalProperties</c> is where it goes.
    /// </remarks>
    [Fact]
    public void Generate_ADictionaryProperty_IsAnObjectWithATypedValue()
    {
        var descriptions = TypeProperties(Generate(Manifest("""
            [{ "operationId": "Catalog.GetProperty", "httpMethod": "GET", "fullRoute": "/properties/{id}",
               "successStatusCode": 200, "isVoid": false, "response": { "type": "Showcase.Catalog.Dtos.PropertyDetailDto" } }]
            """,
            """
            [{ "type": "Showcase.Catalog.Dtos.PropertyDetailDto", "simpleName": "PropertyDetailDto", "kind": "dto",
               "properties": [{ "name": "Descriptions",
                                "type": "System.Collections.Generic.IReadOnlyDictionary<string, string>",
                                "isRequired": false, "isNullable": true }] }]
            """)), "PropertyDetailDto").GetProperty("descriptions");

        Types(descriptions).Should().Equal("object", "null");
        descriptions.GetProperty("additionalProperties").GetProperty("type").GetString().Should().Be("string");
    }

    /// <remarks>
    ///     <c>byte[]</c> is the exception to the array rule and a stream is the exception to everything:
    ///     both cross the wire as strings, and saying "array of integer" for a base64 payload would be a
    ///     new lie in place of the old one.
    /// </remarks>
    [Fact]
    public void Generate_BinaryPayloads_AreStringsWithAFormat()
    {
        var properties = TypeProperties(Generate(Manifest("""
            [{ "operationId": "Docs.Download", "httpMethod": "GET", "fullRoute": "/docs/{id}",
               "successStatusCode": 200, "isVoid": false, "response": { "type": "Pragmatic.Endpoints.Responses.FileResponse" } }]
            """,
            """
            [{ "type": "Pragmatic.Endpoints.Responses.FileResponse", "simpleName": "FileResponse", "kind": "dto",
               "properties": [
                 { "name": "Content", "type": "System.IO.Stream", "isRequired": true, "isNullable": false },
                 { "name": "Thumbnail", "type": "byte[]", "isRequired": false, "isNullable": true }] }]
            """)), "FileResponse");

        Types(properties.GetProperty("content")).Should().Equal("string");
        properties.GetProperty("content").GetProperty("format").GetString().Should().Be("binary");
        Types(properties.GetProperty("thumbnail")).Should().Equal("string", "null");
        properties.GetProperty("thumbnail").GetProperty("format").GetString().Should().Be("byte");
    }

    /// <summary>
    ///     A type the manifest does not describe gets an empty schema, not a string.
    /// </summary>
    /// <remarks>
    ///     An empty schema means "any value" in JSON Schema, and that is the true statement: the document
    ///     does not know this type. <c>"type": "string"</c> is a false one, and a client generator
    ///     believes it — which is the whole difference between a contract that is incomplete and a
    ///     contract that is wrong. An enum still falls back to <c>string</c>, because that is what
    ///     <c>JsonStringEnumConverter</c> actually writes.
    /// </remarks>
    [Fact]
    public void Generate_AnUndescribedType_SaysNothingRatherThanString()
    {
        var properties = RequestProperties(Generate(Manifest("""
            [{
              "operationId": "Ops.Push", "httpMethod": "POST", "fullRoute": "/push",
              "successStatusCode": 200, "isVoid": false,
              "requestBody": { "properties": [
                { "name": "Payload", "type": "Some.Foreign.Sdk.Envelope", "isRequired": true, "isNullable": false },
                { "name": "Day", "type": "System.DayOfWeek", "isRequired": false, "isNullable": true, "isEnum": true }
              ]}
            }]
            """)), "PushRequest");

        properties.GetProperty("payload").TryGetProperty("type", out _).Should().BeFalse(
            "a type the manifest does not carry is described by saying nothing about it");
        // An enum outside the manifest still has a known shape on the wire, and this one is optional.
        Types(properties.GetProperty("day")).Should().Equal("string", "null");
    }

    /// <summary>
    ///     A nullable reference to another schema is a union, because a <c>$ref</c> cannot be qualified.
    /// </summary>
    /// <remarks>
    ///     The eleven cases in the reference application where a property points at another schema and
    ///     may be absent — an optional parent, an unloaded navigation. Written as a bare <c>$ref</c>
    ///     beside a <c>nullable</c> flag, both halves were lost: the flag is not a 3.1 keyword, and a
    ///     reader that honoured it could not have applied it to a reference anyway.
    /// </remarks>
    [Fact]
    public void Generate_ANullableRefProperty_IsAUnionWithNull()
    {
        var parent = TypeProperties(Generate(Manifest("""
            [{ "operationId": "Catalog.GetCategory", "httpMethod": "GET", "fullRoute": "/categories/{id}",
               "successStatusCode": 200, "isVoid": false, "response": { "type": "Showcase.Catalog.Entities.Category" } }]
            """,
            """
            [{ "type": "Showcase.Catalog.Entities.Category", "simpleName": "Category", "kind": "entity",
               "properties": [
                 { "name": "Name", "type": "string", "isRequired": true, "isNullable": false },
                 { "name": "Parent", "type": "Showcase.Catalog.Entities.Category", "isRequired": false, "isNullable": true }] }]
            """)), "Category").GetProperty("parent");

        var union = parent.GetProperty("anyOf").EnumerateArray().ToList();
        union.Should().HaveCount(2);
        union[0].GetProperty("$ref").GetString().Should().Be("#/components/schemas/Category");
        union[1].GetProperty("type").GetString().Should().Be("null");
    }

    /// <summary>
    ///     A query parameter that takes a list is an array, like everywhere else.
    /// </summary>
    /// <remarks>
    ///     The framework documents <c>?names=a&amp;names=b</c>, and the parameter half of the document
    ///     went through the same seven-primitive mapping the schemas did — so the one place a caller
    ///     reads how to pass a list said to pass a string.
    /// </remarks>
    [Fact]
    public void Generate_AnArrayQueryParameter_IsAnArray()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Catalog.SearchAmenities", "httpMethod": "GET", "fullRoute": "/amenities/search",
              "successStatusCode": 200, "isVoid": false,
              "parameters": [
                { "name": "names", "in": "query", "type": "System.Collections.Generic.List<string>", "isRequired": false },
                { "name": "page", "in": "query", "type": "int", "isRequired": false }
              ]
            }]
            """));

        var parameters = doc.RootElement.GetProperty("paths").GetProperty("/amenities/search")
            .GetProperty("get").GetProperty("parameters");

        var names = parameters.EnumerateArray().First(p => p.GetProperty("name").GetString() == "names");
        names.GetProperty("schema").GetProperty("type").GetString().Should().Be("array");
        names.GetProperty("schema").GetProperty("items").GetProperty("type").GetString().Should().Be("string");

        var page = parameters.EnumerateArray().First(p => p.GetProperty("name").GetString() == "page");
        page.GetProperty("schema").GetProperty("type").GetString().Should().Be("integer",
            "a scalar parameter keeps the shape it had");
    }

    /// <summary>
    ///     A response schema names what the response carries, not what the class calls it.
    /// </summary>
    /// <remarks>
    ///     The request half of this was closed first, because an application had a renamed import field
    ///     and the mismatch showed up as a refused request. The response half stayed open a round
    ///     longer for the opposite reason: nothing renamed a response field, so there was nothing to
    ///     see. It is the worse half. A request sent under the wrong name is refused and the caller
    ///     learns something; a response read under the wrong name deserialises to a default, and the
    ///     client reports nothing at all.
    /// </remarks>
    [Fact]
    public void Generate_AResponseTypeWithAWireName_PublishesTheWireName()
    {
        var properties = TypeProperties(Generate(Manifest("""
            [{ "operationId": "Catalog.GetThing", "httpMethod": "GET", "fullRoute": "/things/{id}",
               "successStatusCode": 200, "isVoid": false, "response": { "type": "Catalogue.Dtos.ThingDto" } }]
            """,
            """
            [{ "type": "Catalogue.Dtos.ThingDto", "simpleName": "ThingDto", "kind": "dto",
               "properties": [
                 { "name": "Name", "wireName": "label", "type": "string", "isRequired": true, "isNullable": false },
                 { "name": "Weight", "type": "int", "isRequired": true, "isNullable": false }] }]
            """)), "ThingDto");

        properties.TryGetProperty("label", out _).Should().BeTrue(
            "the client is generated from this, and it must read the field the response carries");
        properties.TryGetProperty("name", out _).Should().BeFalse();
        properties.TryGetProperty("weight", out _).Should().BeTrue(
            "a property that is not renamed keeps its camel-cased name");
    }

    /// <summary>
    ///     Every <c>$ref</c> the document writes points at a component the document emits.
    /// </summary>
    /// <remarks>
    ///     The property renderers only began writing references when they stopped publishing every
    ///     non-primitive as a string, and a reference is the one construct in this document that can
    ///     be well-formed and still broken: it parses, it reads, and every client generator fails on a
    ///     name it cannot find. The catalogue is supposed to make that impossible — it resolves a type
    ///     to a key it has reserved, or answers null — so this asserts the property the design claims
    ///     rather than trusting it.
    /// </remarks>
    [Fact]
    public void Generate_EveryReference_ResolvesToAnEmittedSchema()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Catalog.GetThing", "httpMethod": "GET", "fullRoute": "/things/{id}",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "Catalogue.Dtos.ThingDto" },
               "parameters": [{ "name": "shelf", "in": "query", "type": "Catalogue.Shelf", "isRequired": false }],
               "requestBody": { "properties": [
                 { "name": "Rows", "type": "System.Collections.Generic.List<Catalogue.Dtos.RowDto>", "isRequired": true, "isNullable": false },
                 { "name": "Parent", "type": "Catalogue.Dtos.ThingDto", "isRequired": false, "isNullable": true }
               ]}
            }]
            """,
            """
            [{ "type": "Catalogue.Dtos.ThingDto", "simpleName": "ThingDto", "kind": "dto",
               "properties": [
                 { "name": "Row", "type": "Catalogue.Dtos.RowDto", "isRequired": false, "isNullable": true },
                 { "name": "Shelf", "type": "Catalogue.Shelf", "isRequired": true, "isNullable": false, "isEnum": true }] },
             { "type": "Catalogue.Dtos.RowDto", "simpleName": "RowDto", "kind": "dto",
               "properties": [{ "name": "Amount", "type": "decimal", "isRequired": true, "isNullable": false }] },
             { "type": "Catalogue.Shelf", "simpleName": "Shelf", "kind": "enum", "values": ["Cold", "Ambient"] }]
            """));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(s => s.Name).ToList();

        var referenced = new List<string>();
        CollectRefs(doc.RootElement, referenced);

        referenced.Should().NotBeEmpty("a corpus of nested types must produce references to check");
        referenced.Distinct().Where(r => !schemas.Contains(r)).Should().BeEmpty(
            "a dangling reference parses and reads, and breaks every client generated from it");
    }

    private static void CollectRefs(JsonElement element, List<string> into)
    {
        const string prefix = "#/components/schemas/";

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref")
                        && property.Value.GetString() is { } target
                        && target.StartsWith(prefix, System.StringComparison.Ordinal))
                        into.Add(target.Substring(prefix.Length));
                    else
                        CollectRefs(property.Value, into);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectRefs(item, into);
                break;
        }
    }

    [Fact]
    public void Generate_ErrorResponses_RefProblemDetails()
    {
        using var doc = Generate(Manifest("""
            [{
              "operationId": "Booking.GetReservation", "httpMethod": "GET", "fullRoute": "/reservations/{id}",
              "successStatusCode": 200, "isVoid": false,
              "errors": [{ "type": "global::App.NotFoundError", "code": "NOT_FOUND", "statusCode": 404 }]
            }]
            """));

        var responses = doc.RootElement.GetProperty("paths").GetProperty("/reservations/{id}")
            .GetProperty("get").GetProperty("responses");

        responses.GetProperty("404").GetProperty("description").GetString().Should().Be("NOT_FOUND");
        responses.GetProperty("404").GetProperty("content").GetProperty("application/problem+json")
            .GetProperty("schema").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/ProblemDetails");
    }

    [Fact]
    public void Generate_VoidEndpoint_HasNoResponseContent()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.Cancel", "httpMethod": "DELETE", "fullRoute": "/reservations/{id}",
               "successStatusCode": 204, "isVoid": true }]
            """));

        var success = doc.RootElement.GetProperty("paths").GetProperty("/reservations/{id}")
            .GetProperty("delete").GetProperty("responses").GetProperty("204");

        success.GetProperty("description").GetString().Should().Be("No content");
        success.TryGetProperty("content", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_ResponseType_IsLinkedToTheMatchingComponentSchema()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.GetReservation", "httpMethod": "GET", "fullRoute": "/reservations/{id}",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::App.ReservationDto" } }]
            """, """
            [{ "type": "global::App.ReservationDto", "simpleName": "ReservationDto", "kind": "dto",
               "properties": [{ "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false }] }]
            """));

        doc.RootElement.GetProperty("paths").GetProperty("/reservations/{id}").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/ReservationDto");

        doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ReservationDto")
            .GetProperty("properties").GetProperty("id").GetProperty("format").GetString().Should().Be("uuid");
    }

    [Fact]
    public void Generate_Examples_AreEmbeddedAsJsonWhenValidAndAsStringsWhenNot()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.CreateReservation", "httpMethod": "POST", "fullRoute": "/reservations",
               "successStatusCode": 201, "isVoid": false,
               "requestBody": { "properties": [{ "name": "GuestId", "type": "System.Guid", "isRequired": true, "isNullable": false }] },
               "requestExamples": [{ "json": "{\"guestId\":\"a\"}" }] },
             { "operationId": "Booking.UpdateReservation", "httpMethod": "PUT", "fullRoute": "/reservations",
               "successStatusCode": 200, "isVoid": false,
               "requestBody": { "properties": [{ "name": "GuestId", "type": "System.Guid", "isRequired": true, "isNullable": false }] },
               "requestExamples": [{ "json": "{ not json" }] }]
            """));

        var content = doc.RootElement.GetProperty("paths").GetProperty("/reservations");

        content.GetProperty("post").GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("example").GetProperty("guestId").GetString()
            .Should().Be("a");

        // An invalid payload (already flagged by PRAG0518) degrades to a string instead of corrupting
        // the document — this is the guard that keeps the whole OpenAPI parseable.
        content.GetProperty("put").GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("example").ValueKind
            .Should().Be(JsonValueKind.String);
    }

    /// <summary>
    ///     The manifest carries enum members under <c>"values"</c> — the name the two other manifest
    ///     consumers already bind to — and only their names, so the schema is a string enum. That is also
    ///     what every enum-typed property maps to elsewhere in this generator.
    /// </summary>
    [Fact]
    public void Generate_EnumType_IsAStringSchemaListingItsMembers()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.GetReservation", "httpMethod": "GET", "fullRoute": "/r",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::App.Status" } }]
            """, """
            [{ "type": "global::App.Status", "simpleName": "Status", "kind": "enum",
               "values": ["Draft", "Confirmed"] }]
            """));

        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("Status");

        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Draft", "Confirmed");
    }

    [Fact]
    public void Generate_TwoModulesDeclaringTheSameSimpleName_GetDistinctSchemaKeys()
    {
        // Two modules with a same-named DTO must not produce two "components.schemas.GuestDto" members:
        // the text still parses, but every JSON reader keeps one of them, chosen arbitrarily. The first
        // declaration keeps the simple name; the homonym falls back to its qualified name.
        var moduleTypes = """
            [{ "type": "global::A.GuestDto", "simpleName": "GuestDto", "kind": "dto",
               "properties": [{ "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false }] }]
            """;
        var endpoints = """
            [{ "operationId": "Booking.Get", "httpMethod": "GET", "fullRoute": "/g",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::A.GuestDto" } }]
            """;

        var json = OpenApiJsonGenerator.Generate("Showcase",
        [
            Manifest(endpoints, moduleTypes),
            // The second module answers with its OWN namesake: replacing only the route would leave the
            // second endpoint answering with the first one's type, so B.GuestDto would be reached by
            // nothing and not published — and the collision case would not arise at all.
            Manifest(
                endpoints.Replace("\"/g\"", "\"/g2\"").Replace("A.GuestDto", "B.GuestDto"),
                moduleTypes.Replace("A.GuestDto", "B.GuestDto"))
        ]).Json;

        using var doc = JsonDocument.Parse(json!);
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        ManifestTestHarness.DuplicateKeys(schemas).Should().BeEmpty();
        schemas.TryGetProperty("GuestDto", out _).Should().BeTrue("the first declaration keeps the simple name");
        schemas.TryGetProperty("B.GuestDto", out _).Should().BeTrue("the homonym is disambiguated by its FQN");
    }

    [Fact]
    public void Generate_TheSameTypeDeclaredByTwoModules_IsEmittedOnce()
    {
        var moduleTypes = """
            [{ "type": "global::Shared.GuestDto", "simpleName": "GuestDto", "kind": "dto",
               "properties": [{ "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false }] }]
            """;
        var endpoints = """
            [{ "operationId": "Booking.Get", "httpMethod": "GET", "fullRoute": "/g",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::A.GuestDto" } }]
            """;

        using var doc = Generate(
            Manifest(endpoints, moduleTypes),
            Manifest(endpoints.Replace("\"/g\"", "\"/g2\""), moduleTypes));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        ManifestTestHarness.DuplicateKeys(schemas).Should().BeEmpty();
        schemas.EnumerateObject().Count(p => p.Name.EndsWith("GuestDto")).Should().Be(1);
    }

    /// <summary>
    ///     A module type called <c>ProblemDetails</c> must not overwrite the error schema every error
    ///     response <c>$ref</c>s.
    /// </summary>
    [Fact]
    public void Generate_ModuleTypeNamedProblemDetails_DoesNotShadowTheErrorSchema()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.Get", "httpMethod": "GET", "fullRoute": "/g",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::App.ProblemDetails" },
               "errors": [{ "type": "global::App.NotFoundError", "code": "NOT_FOUND", "statusCode": 404 }] }]
            """, """
            [{ "type": "global::App.ProblemDetails", "simpleName": "ProblemDetails", "kind": "dto",
               "properties": [{ "name": "Custom", "type": "string", "isRequired": true, "isNullable": false }] }]
            """));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        ManifestTestHarness.DuplicateKeys(schemas).Should().BeEmpty();
        schemas.GetProperty("ProblemDetails").GetProperty("properties").TryGetProperty("code", out _)
            .Should().BeTrue("the reserved schema is the error one");
        schemas.GetProperty("App.ProblemDetails").GetProperty("properties").TryGetProperty("custom", out _)
            .Should().BeTrue();
    }

    // ---------------------------------------------------------------- query response envelopes

    [Fact]
    public void Generate_UnpagedQueryResponse_IsAnArrayOfTheItemSchema()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.List", "httpMethod": "GET", "fullRoute": "/reservations",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::System.Collections.Generic.IReadOnlyList<global::App.GuestDto>" } }]
            """, """
            [{ "type": "global::App.GuestDto", "simpleName": "GuestDto", "kind": "dto",
               "properties": [{ "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false }] }]
            """));

        var schema = doc.RootElement.GetProperty("paths").GetProperty("/reservations").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        schema.GetProperty("type").GetString().Should().Be("array");
        schema.GetProperty("items").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/GuestDto");
    }

    [Fact]
    public void Generate_PagedQueryResponse_RefsAPagedEnvelopeSchema()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.Search", "httpMethod": "GET", "fullRoute": "/reservations",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::Pragmatic.Persistence.Query.Results.PagedResult<global::App.GuestDto>",
                             "isPaged": true } }]
            """, """
            [{ "type": "global::App.GuestDto", "simpleName": "GuestDto", "kind": "dto",
               "properties": [{ "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false }] }]
            """));

        doc.RootElement.GetProperty("paths").GetProperty("/reservations").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/PagedResultOfGuestDto");

        var envelope = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PagedResultOfGuestDto");
        envelope.GetProperty("type").GetString().Should().Be("object");
        envelope.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("$ref")
            .GetString().Should().Be("#/components/schemas/GuestDto");
        envelope.GetProperty("properties").GetProperty("totalCount").GetProperty("type").GetString()
            .Should().Be("integer");
        envelope.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("items");
    }

    [Fact]
    public void Generate_ResponseWrappingAnUnknownItem_HasNoSuccessContent()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.List", "httpMethod": "GET", "fullRoute": "/reservations",
               "successStatusCode": 200, "isVoid": false,
               "response": { "type": "global::System.Collections.Generic.IReadOnlyList<global::App.Unknown>" } }]
            """));

        doc.RootElement.GetProperty("paths").GetProperty("/reservations").GetProperty("get")
            .GetProperty("responses").GetProperty("200").TryGetProperty("content", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_SameRouteDifferentVerbs_AreMergedUnderOnePathItem()
    {
        using var doc = Generate(Manifest("""
            [{ "operationId": "Booking.Get", "httpMethod": "GET", "fullRoute": "/reservations",
               "successStatusCode": 200, "isVoid": false },
             { "operationId": "Booking.Create", "httpMethod": "POST", "fullRoute": "/reservations",
               "successStatusCode": 201, "isVoid": false }]
            """));

        var path = doc.RootElement.GetProperty("paths").GetProperty("/reservations");
        path.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("get", "post");
    }

    /// <summary>
    ///     The declared type(s) of a schema. In OpenAPI 3.1 a nullable property is a union, so
    ///     <c>type</c> is an array — read here rather than asserted around, because the union is the
    ///     thing under test.
    /// </summary>
    private static List<string> Types(JsonElement schema)
        => schema.GetProperty("type") is { ValueKind: JsonValueKind.Array } union
            ? [.. union.EnumerateArray().Select(e => e.GetString()!)]
            : [schema.GetProperty("type").GetString()!];

    private static JsonElement RequestProperties(JsonDocument doc, string schema)
        => doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(schema).GetProperty("properties");

    private static JsonElement TypeProperties(JsonDocument doc, string schema)
        => doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(schema).GetProperty("properties");
}
