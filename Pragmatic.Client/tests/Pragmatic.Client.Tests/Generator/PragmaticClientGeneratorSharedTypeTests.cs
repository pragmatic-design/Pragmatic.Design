using Pragmatic.Testing.Assertions;

namespace Pragmatic.Client.Tests.Generator;

/// <summary>
///     A type reachable from more than one boundary appears in every one of their manifests. Emitting it once
///     per manifest made <c>AddSource</c> throw on the duplicate hint name, and that exception discarded the
///     generation of an ENTIRE client — the boundary lost all its DTOs and interfaces, surfacing as dozens of
///     CS0246 at the call site rather than as anything pointing at the manifest.
/// </summary>
public class PragmaticClientGeneratorSharedTypeTests : PragmaticClientGeneratorTestBase
{
    /// <param name="assembly">Manifest assembly name.</param>
    /// <param name="operationId">Operation returning the shared type.</param>
    /// <param name="properties">The shared type's properties, as manifest JSON.</param>
    private static string ManifestSharing(string assembly, string operationId, string properties)
        => $$"""
            {
                "assembly": "{{assembly}}",
                "endpoints": [
                    {
                        "operationId": "{{operationId}}",
                        "httpMethod": "Get",
                        "fullRoute": "/api/{{assembly}}/shared",
                        "isVoid": false,
                        "response": { "type": "global::Shared.Types.SharedThing" }
                    }
                ],
                "types": [
                    {
                        "type": "global::Shared.Types.SharedThing",
                        "simpleName": "SharedThing",
                        "kind": "dto",
                        "properties": [{{properties}}]
                    }
                ]
            }
            """;

    private const string TwoProps = """
        { "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false },
        { "name": "Name", "type": "string", "isRequired": true, "isNullable": false }
        """;

    [Fact]
    public void SharedTypeInTwoManifests_EmitsItOnce_AndGeneratesBothClients()
    {
        var result = RunGeneratorWithManifests([
            ManifestSharing("Alpha", "Alpha.GetThing", TwoProps),
            ManifestSharing("Beta", "Beta.GetThing", TwoProps)
        ]);

        var sources = GetGeneratedSources(result);

        sources.Keys.Should().ContainSingle(k => k == "SharedThingDto.g.cs");
        GetDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2300",
            "a duplicate hint must not be reported as an unreadable manifest");
        GetDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2304",
            "the two descriptions agree, so there is nothing to warn about");

        // The failure that matters: the second boundary's client must not disappear.
        sources.Keys.Should().Contain(k => k.Contains("Alpha"));
        sources.Keys.Should().Contain(k => k.Contains("Beta"));
    }

    /// <summary>
    ///     The assembly that DECLARES an entity sees only its hand-written members — the generated ones (Id,
    ///     audit, soft-delete) do not exist yet in that compilation — while an assembly that REFERENCES it
    ///     reads them from compiled metadata. One description is then a subset of the other, and the output
    ///     must not depend on which manifest happened to be read first.
    /// </summary>
    [Fact]
    public void SharedTypeDescribedMoreFullyByOneManifest_KeepsTheCompleteShape_EitherOrder()
    {
        const string fewProps = """
            { "name": "Name", "type": "string", "isRequired": true, "isNullable": false }
            """;
        const string manyProps = """
            { "name": "Name", "type": "string", "isRequired": true, "isNullable": false },
            { "name": "Id", "type": "System.Guid", "isRequired": true, "isNullable": false }
            """;

        foreach (var manifests in new[]
                 {
                     new[] { ManifestSharing("Alpha", "Alpha.GetThing", fewProps), ManifestSharing("Beta", "Beta.GetThing", manyProps) },
                     new[] { ManifestSharing("Beta", "Beta.GetThing", manyProps), ManifestSharing("Alpha", "Alpha.GetThing", fewProps) }
                 })
        {
            var result = RunGeneratorWithManifests(manifests);
            var dto = GetGeneratedSources(result)["SharedThingDto.g.cs"];

            dto.Should().Contain("Name").And.Contain("Id",
                "the complete description is the truth about the type, whichever manifest carried it");
            GetDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2304",
                "a subset is partial information, not a conflict");
        }
    }

    [Fact]
    public void SameNameDescribingTwoDifferentShapes_ReportsPrag2304()
    {
        const string oneShape = """
            { "name": "Code", "type": "string", "isRequired": true, "isNullable": false }
            """;
        const string otherShape = """
            { "name": "Total", "type": "decimal", "isRequired": true, "isNullable": false }
            """;

        var result = RunGeneratorWithManifests([
            ManifestSharing("Alpha", "Alpha.GetThing", oneShape),
            ManifestSharing("Beta", "Beta.GetThing", otherShape)
        ]);

        GetDiagnostics(result).Should().ContainSingle(d => d.Id == "PRAG2304")
            .Which.GetMessage().Should().Contain("SharedThingDto");
    }
}
