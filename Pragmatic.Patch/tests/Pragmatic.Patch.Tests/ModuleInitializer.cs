using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VerifyTests;

namespace Pragmatic.Patch.Tests;

/// <summary>
///     Module initializer for Verify configuration.
/// </summary>
public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // Store snapshots in Snapshots subfolder next to test file
        DerivePathInfo((sourceFile, projectDirectory, type, method) =>
        {
            var directory = Path.GetDirectoryName(sourceFile)!;
            var snapshotsDir = Path.Combine(directory, "Snapshots");
            return new PathInfo(snapshotsDir, type.Name, method.Name);
        });

        // Scrub generator version so snapshot tests don't break on version bumps
        VerifierSettings.AddScrubber(builder =>
        {
            var result = Regex.Replace(builder.ToString(), @" v\d+\.\d+\.\d+", " v*");
            builder.Clear();
            builder.Append(result);
        });
    }
}
