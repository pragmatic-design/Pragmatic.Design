using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Pragmatic.Client.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.AddScrubber(builder =>
        {
            var result = Regex.Replace(builder.ToString(), @" v\d+\.\d+\.\d+", " v*");
            builder.Clear();
            builder.Append(result);
        });
    }
}
