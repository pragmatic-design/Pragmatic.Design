using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VerifyTests;

namespace Pragmatic.Composition.Tests;

/// <summary>
///     Module initializer for Verify snapshot tests.
///     Registers the version scrubber required by project rule 3.4 so snapshot tests
///     do not break on version bumps (e.g. a generated " v1.2.3" becomes " v*").
/// </summary>
public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.AddScrubber(builder =>
        {
            var result = Regex.Replace(builder.ToString(), @" v\d+\.\d+\.\d+", " v*");
            builder.Clear();
            builder.Append(result);
        });
    }
}
