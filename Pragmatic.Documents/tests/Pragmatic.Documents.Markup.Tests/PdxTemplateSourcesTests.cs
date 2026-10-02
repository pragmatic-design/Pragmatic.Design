using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
///     Where a template is found by name: inside a module's assembly, in a directory, or the first of
///     several — an organisation's own before the application's.
/// </summary>
public sealed class PdxTemplateSourcesTests
{
    private readonly EmbeddedPdxTemplateSource _embedded = new(typeof(PdxTemplateSourcesTests).Assembly);

    [Fact]
    public async Task AnEmbeddedTemplate_IsFoundByItsFileName()
    {
        var markup = await _embedded.FindAsync("receipt.pdxdoc");

        markup.Should().NotBeNull();
        markup.Should().Contain("<document");
    }

    [Fact]
    public async Task AFileNameWithAHyphen_IsFoundAsWritten()
    {
        (await _embedded.FindAsync("mail-header.pdxemail")).Should().NotBeNull();
    }

    [Fact]
    public async Task AnUnknownName_IsNotFound()
    {
        (await _embedded.FindAsync("nowhere.pdxdoc")).Should().BeNull();
    }

    /// <summary>
    ///     Two files with one name in two folders: the bare name is refused rather than answered with
    ///     whichever the manifest lists first, and the folder settles it.
    /// </summary>
    [Fact]
    public async Task ANameTwoFoldersShare_IsRefused_AndTheFolderSettlesIt()
    {
        var bare = async () => await _embedded.FindAsync("twin.pdxdoc");
        await bare.Should().ThrowAsync<InvalidOperationException>();

        (await _embedded.FindAsync("north/twin.pdxdoc")).Should().Contain("north");
        (await _embedded.FindAsync("south/twin.pdxdoc")).Should().Contain("south");
    }

    [Fact]
    public async Task ADirectory_IsReadByRelativeName()
    {
        var directory = Directory.CreateTempSubdirectory("pdx-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "note.pdxdoc"), "<document />");

            var source = new DirectoryPdxTemplateSource(directory.FullName);

            (await source.FindAsync("note.pdxdoc")).Should().Be("<document />");
            (await source.FindAsync("absent.pdxdoc")).Should().BeNull();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>A name that climbs out of the directory is not read, whatever is there.</summary>
    [Fact]
    public async Task ADirectory_RefusesANameOutsideIt()
    {
        var directory = Directory.CreateTempSubdirectory("pdx-");
        try
        {
            var source = new DirectoryPdxTemplateSource(directory.FullName);

            var climbing = async () => await source.FindAsync("../secret.pdxdoc");

            await climbing.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TheFirstSourceThatHasIt_Wins()
    {
        var own = new FixedSource("receipt.pdxdoc", "<document>own</document>");
        var source = new FirstFoundPdxTemplateSource(own, _embedded);

        (await source.FindAsync("receipt.pdxdoc")).Should().Be("<document>own</document>");
        (await source.FindAsync("letterhead.pdxdoc")).Should().Contain("shop.name",
            "a name the first does not have falls through to the next");
    }

    private sealed class FixedSource(string name, string markup) : IPdxTemplateSource
    {
        public ValueTask<string?> FindAsync(string wanted, CancellationToken ct = default)
            => ValueTask.FromResult(wanted == name ? markup : null);
    }
}
