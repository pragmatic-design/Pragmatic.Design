using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>Where the files the tests read live in the repository.</summary>
/// <remarks>
///     From <c>[CallerFilePath]</c>, not from <c>AppContext.BaseDirectory</c>: the gate builds into <c>artifacts/build/bin/…</c>, where no
///     ancestor of the running assembly is this project, so a path counted up from the output found nothing under the gate and everything
///     anywhere else. The compiler knows where the source is.
/// </remarks>
internal static class TestPaths
{
    /// <summary>This test project's directory.</summary>
    public static string Project { get; } = Here();

    /// <summary>A path below <c>Pragmatic.Persistence/tests/Fixtures</c>.</summary>
    public static string Fixture(params string[] parts) => Path.GetFullPath(Path.Combine([Project, "..", "Fixtures", .. parts]));

    /// <summary>A path below <c>Pragmatic.Persistence/samples</c>.</summary>
    public static string Sample(params string[] parts) => Path.GetFullPath(Path.Combine([Project, "..", "..", "samples", .. parts]));

    private static string Here([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;
}
