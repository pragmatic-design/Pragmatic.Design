using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.OpenApi;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     Manifest parsing: per-module documents pass through, the host-aggregated form
///     (a wrapper with "modules": [...]) is flattened so consumers always see per-module documents.
/// </summary>
public class ManifestReaderTests
{
    [Fact]
    public void Parse_ModuleManifest_ReturnsSingleDocument()
    {
        const string json = """
            {
              "$schema": "pragmatic-manifest/v1",
              "version": "1.0.0",
              "assembly": "Showcase.Booking",
              "endpoints": [
                { "operationId": "Booking.CreateReservation", "httpMethod": "POST", "fullRoute": "/api/reservations" }
              ]
            }
            """;

        var docs = ManifestReader.Parse([json]);

        docs.Should().HaveCount(1);
        docs[0].Assembly.Should().Be("Showcase.Booking");
        docs[0].Endpoints.Should().ContainSingle(e => e.OperationId == "Booking.CreateReservation");
    }

    [Fact]
    public void Parse_AggregatedHostManifest_FlattensModules()
    {
        const string json = """
            {
              "$schema": "pragmatic-manifest/v1",
              "version": "1.0.0",
              "assembly": "Showcase.Host",
              "aggregated": true,
              "moduleCount": 2,
              "modules": [
                {
                  "assembly": "Showcase.Booking",
                  "endpoints": [ { "operationId": "Booking.CreateReservation", "httpMethod": "POST", "fullRoute": "/api/reservations" } ]
                },
                {
                  "assembly": "Showcase.Billing",
                  "endpoints": [ { "operationId": "Billing.CreateInvoice", "httpMethod": "POST", "fullRoute": "/api/invoices" } ]
                }
              ]
            }
            """;

        var docs = ManifestReader.Parse([json]);

        docs.Should().HaveCount(2);
        docs.Select(d => d.Assembly).Should().BeEquivalentTo("Showcase.Booking", "Showcase.Billing");
        docs.SelectMany(d => d.Endpoints!).Should().HaveCount(2);
    }

    [Fact]
    public void Parse_EndpointWithDescriptionAndDefaultValue_Deserialized()
    {
        const string json = """
            {
              "assembly": "Showcase.Booking",
              "endpoints": [
                {
                  "operationId": "Booking.ListReservations",
                  "httpMethod": "GET",
                  "fullRoute": "/api/reservations",
                  "summary": "List reservations",
                  "description": "Returns the paged reservation list.",
                  "parameters": [
                    { "name": "pageSize", "in": "query", "type": "int", "isRequired": false, "defaultValue": "20" }
                  ]
                }
              ]
            }
            """;

        var docs = ManifestReader.Parse([json]);

        var ep = docs.Should().ContainSingle().Which.Endpoints.Should().ContainSingle().Which;
        ep.Description.Should().Be("Returns the paged reservation list.");
        ep.Parameters.Should().ContainSingle().Which.DefaultValue.Should().Be("20");
    }

    private const string Booking =
        """{"assembly":"Showcase.Booking","endpoints":[{"operationId":"Booking.CreateReservation","httpMethod":"POST","fullRoute":"/api/reservations"}]}""";

    private const string Billing =
        """{"assembly":"Showcase.Billing","endpoints":[{"operationId":"Billing.CreateInvoice","httpMethod":"POST","fullRoute":"/api/invoices"}]}""";

    /// <summary>What a Composition host registers: the module manifests embedded verbatim.</summary>
    private static string Aggregated(params string[] modules) => $$"""
        {
          "assembly": "Showcase.Host",
          "aggregated": true,
          "modules": [{{string.Join(",", modules)}}]
        }
        """;

    /// <summary>
    ///     A module registers its own manifest, and a Composition host embeds the same string in its
    ///     aggregated one: read as two sources, every endpoint of that module would be listed twice —
    ///     and the MCP catalogue numbers a second copy of a tool instead of dropping it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parse_AModuleRegisteredByItselfAndByItsHost_IsReadOnce(bool moduleFirst)
    {
        string[] registered = moduleFirst
            ? [Booking, Aggregated(Booking, Billing)]
            : [Aggregated(Booking, Billing), Booking];

        var docs = ManifestReader.Parse(registered);

        docs.Select(d => d.Assembly).Should().BeEquivalentTo("Showcase.Booking", "Showcase.Billing");
    }

    /// <summary>
    ///     The control: identity is the manifest itself, not its <c>assembly</c> label, which is derived
    ///     from a namespace and can be shared by two assemblies — both are kept.
    /// </summary>
    [Fact]
    public void Parse_TwoManifestsWithTheSameLabel_AreBothKept()
    {
        var other = Billing.Replace("Showcase.Billing", "Showcase.Booking");

        var docs = ManifestReader.Parse([Booking, Aggregated(other)]);

        docs.Should().HaveCount(2);
        docs.SelectMany(d => d.Endpoints!).Select(e => e.OperationId)
            .Should().BeEquivalentTo("Booking.CreateReservation", "Billing.CreateInvoice");
    }

    [Fact]
    public void Parse_EmptyOrInvalidEntries_AreSkippedWithoutThrowing()
    {
        var docs = ManifestReader.Parse(["null", "{ }"]);

        // "null" deserializes to null (skipped); "{}" is a valid empty document.
        docs.Should().HaveCount(1);
    }
}
