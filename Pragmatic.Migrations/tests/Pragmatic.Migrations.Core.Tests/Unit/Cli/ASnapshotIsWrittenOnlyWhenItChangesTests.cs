using System.IO.MemoryMappedFiles;
using Pragmatic.Migrations.Cli.Snapshot;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     <c>snapshot</c> rewrites a schema file only when its content changes — so a build that regenerates the same
///     schema touches nothing, and cannot collide with a process that has the file mapped.
/// </summary>
/// <remarks>
///     <para>
///         Time off's build runs <c>snapshot</c> after every build, and the gate failed at random with
///         <c>IOException: … a file with a user-mapped section open</c> on <c>AppDatabase.schema.json</c>
///         (<c>ERROR_USER_MAPPED_FILE</c>): another process — a git client hashing the working tree, an indexer — held
///         a mapped view of the file the previous build had just rewritten, and truncating a mapped file fails on
///         Windows. The content was the same both times: the gate builds the same source twice.
///     </para>
///     <para>
///         The view is opened the way such a reader opens it — sharing read, write and delete — so the write fails
///         for the mapping, not for a sharing violation this test would have made up. On Linux a mapped view does not
///         block a write, and the first case only measures the unchanged file.
///     </para>
/// </remarks>
public sealed class ASnapshotIsWrittenOnlyWhenItChangesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.schema.json");

    [Fact]
    public async Task AnUnchangedSnapshot_IsNotRewritten_WhileAnotherProcessMapsIt()
    {
        await File.WriteAllTextAsync(_path, """{ "hash": "abc" }""");
        var before = File.GetLastWriteTimeUtc(_path);

        bool written;
        using (var mapping = MappedReadOnly(_path))
        using (mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            written = await SchemaSnapshotWriter.WriteAsync(_path, """{ "hash": "abc" }""");

        written.Should().BeFalse("the schema did not change, so there is nothing to write");
        File.GetLastWriteTimeUtc(_path).Should().Be(before, "an unchanged snapshot keeps its timestamp");
    }

    /// <summary>The control: "not rewritten" is satisfied by a writer that never writes.</summary>
    [Fact]
    public async Task AChangedSnapshot_IsWritten()
    {
        await File.WriteAllTextAsync(_path, """{ "hash": "abc" }""");

        var written = await SchemaSnapshotWriter.WriteAsync(_path, """{ "hash": "def" }""");

        written.Should().BeTrue();
        (await File.ReadAllTextAsync(_path)).Should().Be("""{ "hash": "def" }""");
    }

    [Fact]
    public async Task AFirstSnapshot_IsWritten()
    {
        var written = await SchemaSnapshotWriter.WriteAsync(_path, """{ "hash": "abc" }""");

        written.Should().BeTrue();
        (await File.ReadAllTextAsync(_path)).Should().Be("""{ "hash": "abc" }""");
    }

    public void Dispose() => File.Delete(_path);

    /// <summary>A read-only mapping of the file; a view on it is what blocks a truncating write.</summary>
    private static MemoryMappedFile MappedReadOnly(string path)
        => MemoryMappedFile.CreateFromFile(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
}
