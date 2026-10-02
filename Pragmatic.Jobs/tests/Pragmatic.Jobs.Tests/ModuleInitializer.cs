using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VerifyTests;

namespace Pragmatic.Jobs.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        DerivePathInfo((sourceFile, projectDirectory, type, method) =>
        {
            var directory = Path.GetDirectoryName(sourceFile)!;
            var snapshotsDir = Path.Combine(directory, "Snapshots");
            return new PathInfo(snapshotsDir, type.Name, method.Name);
        });

        VerifierSettings.AddScrubber(builder =>
        {
            var result = Regex.Replace(builder.ToString(), @" v\d+\.\d+\.\d+", " v*");
            builder.Clear();
            builder.Append(result);
        });
    }
}
