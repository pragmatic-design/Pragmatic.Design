using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.VisualTesting.Tests;

/// <summary>
///     A reference without a manifest entry fails, and so does an entry without its file or without one of
///     origin, licence, producedBy and date: nothing is compared against an artefact nobody can trace.
/// </summary>
public sealed class EveryReferenceHasItsProvenanceTests : IDisposable
{
    private const string OneEntry = """
        {
          "references": {
            "a.png": { "origin": "a tool", "licence": "CC0", "producedBy": "a script, version 1", "date": "2026-11-25" }
          }
        }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("pragmatic-references-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheReferencesOfThisSuite_AreEachListedWithTheirProvenance() => ReferenceManifest.AssertComplete(Fixtures.Directory);

    [Fact]
    public void AManifestThatListsEveryFile_IsComplete()
    {
        File.WriteAllText(Path.Combine(_directory, ReferenceManifest.FileName), OneEntry);
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), [1]);

        ReferenceManifest.Problems(_directory).Should().BeEmpty();
    }

    [Fact]
    public void AReferenceWithNoEntry_Fails()
    {
        File.WriteAllText(Path.Combine(_directory, ReferenceManifest.FileName), OneEntry);
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), [1]);
        File.WriteAllBytes(Path.Combine(_directory, "b.png"), [1]);

        ReferenceManifest.Problems(_directory).Should().Equal("b.png: a file with no entry");
        Record(() => ReferenceManifest.AssertComplete(_directory))!.Message.Should().Contain("b.png: a file with no entry");
    }

    [Fact]
    public void AnEntryWithNoFile_Fails()
    {
        File.WriteAllText(Path.Combine(_directory, ReferenceManifest.FileName), OneEntry);

        ReferenceManifest.Problems(_directory).Should().Equal("a.png: listed, but there is no such file");
    }

    [Fact]
    public void AnEntryWithNoLicence_Fails()
    {
        File.WriteAllText(Path.Combine(_directory, ReferenceManifest.FileName), OneEntry.Replace("\"licence\": \"CC0\", ", "", StringComparison.Ordinal));
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), [1]);

        ReferenceManifest.Problems(_directory).Should().Equal("a.png: no \"licence\"");
    }

    [Fact]
    public void ADirectoryWithNoManifest_Fails()
    {
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), [1]);

        ReferenceManifest.Problems(_directory).Should().Equal($"there is no {ReferenceManifest.FileName} in {_directory}");
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
