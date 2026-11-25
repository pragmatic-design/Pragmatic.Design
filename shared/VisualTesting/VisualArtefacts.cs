// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     Where a failed comparison leaves its images: <c>artifacts/visual/</c> at the root of the repository,
///     which CI uploads when the gate fails, or the directory in <c>PRAGMATIC_VISUAL_OUTPUT</c> when it is set.
/// </summary>
public static class VisualArtefacts
{
    public const string DirectoryVariable = "PRAGMATIC_VISUAL_OUTPUT";

    private const string RootMarker = "Pragmatic.Design.slnx";

    public static string Directory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (!string.IsNullOrEmpty(configured))
                return configured;

            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, RootMarker)))
                    return Path.Combine(directory.FullName, "artifacts", "visual");
            }

            return Path.Combine(AppContext.BaseDirectory, "visual");
        }
    }

    /// <summary>Writes the actual, the expected and the amplified difference image, and returns their paths.</summary>
    public static (string Actual, string Expected, string Difference) Write(string name, RgbaImage actual, RgbaImage expected, string? outputDirectory = null)
    {
        var directory = outputDirectory ?? Directory;
        System.IO.Directory.CreateDirectory(directory);
        var stem = Path.Combine(directory, string.Concat(name.Split(Path.GetInvalidFileNameChars())));
        var paths = ($"{stem}.actual.png", $"{stem}.expected.png", $"{stem}.difference.png");
        DiagnosticPng.Write(actual, paths.Item1);
        DiagnosticPng.Write(expected, paths.Item2);
        DiagnosticPng.Write(AmplifiedDifference(actual, expected), paths.Item3);
        return paths;
    }

    /// <summary>
    ///     The absolute difference per colour channel, multiplied by 8 and capped at 255, on opaque black:
    ///     a difference of one level shows as eight, so a faint shift is visible and a large one saturates.
    /// </summary>
    public static RgbaImage AmplifiedDifference(RgbaImage actual, RgbaImage expected)
    {
        RgbaImage.RequireSameSize(actual, expected);
        var pixels = new byte[actual.Pixels.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Min(255, 8 * Math.Abs(actual.Pixels[i + c] - expected.Pixels[i + c]));
            pixels[i + 3] = 255;
        }

        return new RgbaImage(actual.Width, actual.Height, pixels);
    }
}
