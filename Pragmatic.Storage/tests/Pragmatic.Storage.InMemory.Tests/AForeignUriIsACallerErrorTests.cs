using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.InMemory.Tests;

/// <summary>
///     A store handed a URI in another provider's shape says so, rather than answering as if the file
///     were merely absent.
/// </summary>
/// <remarks>
///     <para>
///         Two questions were being answered by one value. "I do not have that file" is
///         <see langword="null" /> and stays <see langword="null" />; "that is not one of my
///         addresses" is a wiring mistake, not a data state, and now says so at the call site — which
///         is what S3, Azure Blob, Google Cloud, FTP and SFTP already did. These two providers were
///         the ones answering the other way.
///     </para>
///     <para>
///         ⚠️ The two URI shapes are not the defect and are not normalised. A local-disk URI is
///         relative because it is servable as a static file; an <c>s3://</c> one is not. Flattening
///         them would remove a real difference to hide a contract that was simply unstated.
///     </para>
///     <para>
///         ⚠️ This class faces both ways on purpose. "Refuses a foreign URI" asserted on one store
///         only would leave the seam as asymmetric as it was, with the asymmetry moved rather than
///         removed.
///     </para>
/// </remarks>
public sealed class AForeignUriIsACallerErrorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-foreign-{Guid.NewGuid():N}");
    private readonly InMemoryFileStorage _memory = new();
    private readonly LocalDiskFileStorage _disk;

    public AForeignUriIsACallerErrorTests()
        => _disk = new LocalDiskFileStorage(_root, NullLogger<LocalDiskFileStorage>.Instance);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private Task<Uri> SavedOnDisk()
        => _disk.SaveAsync(new MemoryStream([1, 2, 3]), "note.txt", "docs");

    private Task<Uri> SavedInMemory()
        => _memory.SaveAsync(new MemoryStream([1, 2, 3]), "note.txt", "docs");

    /// <summary>The setpoint: the disk's URI, asked of the memory store.</summary>
    [Fact]
    public async Task AUriWrittenByTheDisk_IsACallerErrorToTheMemoryStore()
    {
        var uri = await SavedOnDisk();

        var act = () => _memory.GetInfoAsync(uri);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri",
                "the caller asked the wrong store, and naming the parameter is what says which");
    }

    /// <summary>The other three refuse it too, because they read the same URI.</summary>
    /// <remarks>
    ///     One resolver decides for all four. A provider that threw from one and answered from another
    ///     would be the original defect at a smaller scale.
    /// </remarks>
    [Fact]
    public async Task TheOtherReadsOfAForeignUri_RefuseTheSameWay()
    {
        var uri = await SavedOnDisk();

        await ((Func<Task>)(() => _memory.GetAsync(uri))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => _memory.ExistsAsync(uri))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => _memory.DeleteAsync(uri))).Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>⚠️ And the other direction, so the contract belongs to the seam and not to one store.</summary>
    [Fact]
    public async Task AUriWrittenInMemory_IsACallerErrorToTheDisk()
    {
        var uri = await SavedInMemory();

        await ((Func<Task>)(() => _disk.GetInfoAsync(uri))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => _disk.GetAsync(uri))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => _disk.ExistsAsync(uri))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => _disk.DeleteAsync(uri))).Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    ///     ⚠️ The first control: a URI the store wrote still resolves.
    /// </summary>
    /// <remarks>
    ///     "Refuses a foreign URI" is satisfied by refusing everything, and a store used through one
    ///     provider would never notice.
    /// </remarks>
    [Fact]
    public async Task AUriTheMemoryStoreWrote_StillResolves()
    {
        var uri = await SavedInMemory();

        var info = await _memory.GetInfoAsync(uri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(3);
    }

    /// <summary>
    ///     ⚠️ The second control, and the reason the change is narrow: a file it wrote and then
    ///     deleted is missing, not an error.
    /// </summary>
    /// <remarks>
    ///     A caller asking about a file it has already deleted is the ordinary case the
    ///     <see langword="null" /> contract exists for. If that answer moved too, every such caller
    ///     would have to catch to learn what it already knew.
    /// </remarks>
    [Fact]
    public async Task AUriItWroteAndDeleted_IsAMissingFile()
    {
        var uri = await SavedInMemory();
        await _memory.DeleteAsync(uri);

        (await _memory.GetInfoAsync(uri)).Should().BeNull();
        (await _memory.ExistsAsync(uri)).Should().BeFalse();
    }

    /// <summary>⚠️ The same control on the disk, whose absent-file answer is a different code path.</summary>
    [Fact]
    public async Task AFileTheDiskWroteAndDeleted_IsAMissingFile()
    {
        var uri = await SavedOnDisk();
        await _disk.DeleteAsync(uri);

        (await _disk.GetInfoAsync(uri)).Should().BeNull();
        (await _disk.ExistsAsync(uri)).Should().BeFalse();
    }

    /// <summary>
    ///     ⚠️ The third control: a traversing path is refused as before, and is not reported as the
    ///     wrong provider.
    /// </summary>
    /// <remarks>
    ///     <c>LocalDiskFileStorage</c> also rejects a path that escapes its root, and that is an attack,
    ///     not a wiring mistake. The two refusals stay distinguishable: this one keeps answering as a
    ///     file it does not have. Collapsing them into one throw would turn a path-traversal attempt
    ///     into "you asked the wrong store", which is the more comfortable of the two readings and the
    ///     wrong one.
    /// </remarks>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("files/../../../etc/passwd")]
    public async Task ATraversingPath_IsAMissingFileAndNotAWrongProvider(string path)
    {
        var uri = new Uri(path, UriKind.Relative);

        (await _disk.GetInfoAsync(uri)).Should().BeNull();
        (await _disk.ExistsAsync(uri)).Should().BeFalse();
        (await _disk.GetAsync(uri)).Should().BeNull();
    }
}
