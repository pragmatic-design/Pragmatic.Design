using System.Diagnostics;

namespace Pragmatic.Migrations.Cli.Discovery;

/// <summary>
///     Resolves the output assembly of a <c>.csproj</c> by building it, so <c>--project</c> can be
///     used instead of pointing at a <c>.dll</c> that the caller has to locate (and remember to
///     rebuild) themselves.
/// </summary>
public static class ProjectAssemblyResolver
{
    /// <summary>
    ///     Builds <paramref name="projectPath" /> and returns the path of the produced assembly.
    /// </summary>
    /// <param name="projectPath">Path to the <c>.csproj</c>.</param>
    /// <param name="configuration">Build configuration. Defaults to Debug.</param>
    /// <exception cref="FileNotFoundException">The project file does not exist.</exception>
    /// <exception cref="InvalidOperationException">The build failed, or its output could not be located.</exception>
    public static string BuildAndResolve(string projectPath, string configuration = "Debug")
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Project not found: {fullPath}");

        var build = RunDotnet($"build \"{fullPath}\" -c {configuration} --nologo -v quiet");
        if (build.ExitCode != 0)
            throw new InvalidOperationException(
                $"Building '{fullPath}' failed with exit code {build.ExitCode}.{Environment.NewLine}{build.Output}");

        // -getProperty asks MSBuild for the evaluated TargetPath instead of guessing the
        // bin/<config>/<tfm>/ layout, which breaks with a custom OutputPath or multi-targeting.
        var query = RunDotnet($"msbuild \"{fullPath}\" -getProperty:TargetPath -p:Configuration={configuration} -nologo -v:quiet");
        if (query.ExitCode != 0)
            throw new InvalidOperationException(
                $"Could not read TargetPath from '{fullPath}'.{Environment.NewLine}{query.Output}");

        var targetPath = query.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        if (targetPath is null || !File.Exists(targetPath))
            throw new InvalidOperationException(
                $"The build of '{fullPath}' did not produce a locatable assembly. Pass --assembly explicitly.");

        return targetPath;
    }

    private static (int ExitCode, string Output) RunDotnet(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, string.Concat(stdout, stderr));
    }
}
