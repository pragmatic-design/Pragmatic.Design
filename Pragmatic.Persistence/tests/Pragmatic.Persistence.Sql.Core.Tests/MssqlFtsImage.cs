using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The SQL Server full-text image of the Sql suites (docker/mssql-fts), built only when no image of its Dockerfile exists yet (#893).
/// </summary>
/// <remarks>
///     <para>
///         Testcontainers builds an image from a Dockerfile on every run by default (<c>PullPolicy.Always</c>), and this one installs 677 MB
///         from packages.microsoft.com. So it is built when missing, and its tag carries the first twelve hex digits of the Dockerfile's
///         SHA-256: a changed Dockerfile is a new tag, which is missing, and is built. An unchanged one reuses the image, here or loaded from
///         the cache CI keeps under the same hash (<c>.github/workflows/ci.yml</c>, which computes the tag the same way).
///     </para>
///     <para>
///         Linked into <c>Pragmatic.Persistence.Sql.Tests</c>, whose fixture starts the same image: one file says what the image is.
///     </para>
/// </remarks>
public static class MssqlFtsImage
{
    /// <summary>The image's repository, without the tag.</summary>
    public const string Repository = "pragmatic-spike/mssql-fts";

    /// <summary>The image built from the Dockerfile in <paramref name="dockerfileDirectory" />, when no image of that Dockerfile exists.</summary>
    /// <param name="dockerfileDirectory">The directory of docker/mssql-fts, from the calling project's directory.</param>
    public static IFutureDockerImage For(string dockerfileDirectory)
    {
        var directory = Path.Combine(CommonDirectoryPath.GetProjectDirectory().DirectoryPath, dockerfileDirectory);
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "Dockerfile"))))[..12];
        return new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(directory)
            .WithName($"{Repository}:2025-{hash}")
            .WithImageBuildPolicy(PullPolicy.Missing)
            .WithCleanUp(false)
            .Build();
    }
}
