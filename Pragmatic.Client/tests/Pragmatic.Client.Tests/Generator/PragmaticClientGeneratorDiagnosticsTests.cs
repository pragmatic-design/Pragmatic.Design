using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Client.Tests.Generator;

/// <summary>
///     Covers PRAG2300-2349. Without them the generator is silent: a broken manifest emits a
///     <c>.g.cs</c> containing one C# comment, a mistyped boundary filter produces nothing at all, and an
///     unknown type quietly becomes <c>object</c> — in every case the build stays green and the problem
///     surfaces far from its cause.
/// </summary>
public class PragmaticClientGeneratorDiagnosticsTests : PragmaticClientGeneratorTestBase
{
    private const string ManifestWithUnknownResponseType = """
        {
            "assembly": "Showcase.Booking",
            "endpoints": [
                {
                    "operationId": "Booking.ScanAvailability",
                    "httpMethod": "Get",
                    "fullRoute": "/api/availability-scan",
                    "isVoid": false,
                    "response": { "type": "global::Showcase.Booking.Dtos.AvailabilityReport" }
                }
            ],
            "types": []
        }
        """;

    [Fact]
    public void MalformedManifest_ReportsPrag2300_InsteadOfEmittingACommentFile()
    {
        var result = RunGeneratorWithManifest("{ \"assembly\": \"Broken\", \"endpoints\": [ { oops ] }");

        GetDiagnostics(result).Should().ContainSingle(d => d.Id == "PRAG2300")
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);

        GetGeneratedSources(result).Keys.Should().NotContain(k => k.Contains("Error.g.cs"),
            "the failure must be a diagnostic, not a comment buried in a generated file");
    }

    [Fact]
    public void UnknownResponseType_ReportsPrag2301_NamingTheType()
    {
        var result = RunGeneratorWithManifest(ManifestWithUnknownResponseType);

        var diagnostic = GetDiagnostics(result).Should().ContainSingle(d => d.Id == "PRAG2301").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Contain("AvailabilityReport").And.Contain("object");
    }

    [Fact]
    public void KnownTypes_DoNotReportPrag2301()
    {
        var manifest = """
            {
                "assembly": "Showcase.Booking",
                "endpoints": [
                    {
                        "operationId": "Booking.GetGuestId",
                        "httpMethod": "Get",
                        "fullRoute": "/api/guests/{id}",
                        "isVoid": false,
                        "response": { "type": "global::System.Guid" },
                        "parameters": [
                            { "name": "id", "in": "path", "type": "global::System.Guid", "isRequired": true }
                        ]
                    }
                ],
                "types": []
            }
            """;

        var result = RunGeneratorWithManifest(manifest);

        GetDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2301");
    }

    /// <summary>
    ///     An endpoint that returns something the manifest never types. Distinct from PRAG2301, and fixed
    ///     elsewhere: the manifest producer could not determine the type, so the client has nothing to name.
    /// </summary>
    [Fact]
    public void NonVoidEndpointWithoutAResponseType_ReportsPrag2303_NamingTheOperation()
    {
        const string manifest = """
            {
                "assembly": "Showcase.Catalog",
                "endpoints": [
                    {
                        "operationId": "Catalog.AmenityNameAutocomplete",
                        "httpMethod": "Get",
                        "fullRoute": "/api/amenities/autocomplete/name",
                        "isVoid": false
                    }
                ],
                "types": []
            }
            """;

        var result = RunGeneratorWithManifest(manifest);

        var diagnostic = GetDiagnostics(result).Should().ContainSingle(d => d.Id == "PRAG2303").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Contain("Catalog.AmenityNameAutocomplete");
        GetDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2301",
            "there is no type name to report — that is precisely what PRAG2303 says");
    }

    [Fact]
    public void VoidEndpoint_DoesNotReportPrag2303()
    {
        const string manifest = """
            {
                "assembly": "Showcase.Catalog",
                "endpoints": [
                    {
                        "operationId": "Catalog.DeleteAmenity",
                        "httpMethod": "Delete",
                        "fullRoute": "/api/amenities/{id}",
                        "isVoid": true,
                        "parameters": [
                            { "name": "id", "in": "path", "type": "global::System.Guid", "isRequired": true }
                        ]
                    }
                ],
                "types": []
            }
            """;

        GetDiagnostics(RunGeneratorWithManifest(manifest)).Should().NotContain(d => d.Id == "PRAG2303");
    }

    [Fact]
    public void BoundaryFilterMatchingNothing_ReportsPrag2302()
    {
        var result = RunGeneratorWithManifest(ManifestWithUnknownResponseType, boundaryFilter: "Typo");

        var diagnostic = GetDiagnostics(result).Should().ContainSingle(d => d.Id == "PRAG2302").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Contain("Typo");
        GetGeneratedSources(result).Should().BeEmpty();
    }

    [Fact]
    public void BoundaryFilterMatching_GeneratesAndReportsNoFilterWarning()
    {
        var result = RunGeneratorWithManifest(ManifestWithUnknownResponseType, boundaryFilter: "Booking");

        GetDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2302");
        GetGeneratedSources(result).Should().NotBeEmpty();
    }
}
