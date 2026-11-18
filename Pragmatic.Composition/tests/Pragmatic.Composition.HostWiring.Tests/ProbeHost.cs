// Pragmatic.Composition.HostWiring.Tests - A generated host, run as a child process

using System.Diagnostics;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A module and its generated host, written beside the suite's assemblies and run as a child process: the child process, and
///     the files it runs from, removed once it is gone.
/// </summary>
/// <remarks>
///     For the cases whose difference exists only on a runtime the suite does not run on, or only in a process of its own. Without a
///     deps file the runtime takes every assembly in the application's directory, which is how the host finds the Pragmatic packages
///     and the module. Every name starts with one of the prefixes <see cref="HostWiringFixture.BuildReferenceSet" /> leaves out.
/// </remarks>
internal sealed class ProbeHost : IAsyncDisposable
{
    private ProbeHost(string modulePath, string hostPath, string workingDirectory)
    {
        ModulePath = modulePath;
        HostPath = hostPath;
        WorkingDirectory = workingDirectory;
    }

    public string ModulePath { get; }

    public string HostPath { get; }

    public string WorkingDirectory { get; }

    public Process Process { get; private set; } = null!;

    /// <param name="name">The assemblies' name, which starts with a run-probe prefix.</param>
    /// <param name="module">The module's image.</param>
    /// <param name="host">The host's image.</param>
    /// <param name="rollForwardToLatestMajor">Run on the newest installed runtime rather than the suite's.</param>
    /// <param name="hostArgs">Arguments for the host, after <c>--urls=http://127.0.0.1:0</c>.</param>
    public static async Task<ProbeHost> StartAsync(
        string name, byte[] module, byte[] host, bool rollForwardToLatestMajor, params string[] hostArgs)
    {
        var directory = AppContext.BaseDirectory;
        var running = new ProbeHost(
            Path.Combine(directory, $"{name}.Module.dll"),
            Path.Combine(directory, $"{name}.Host.dll"),
            Directory.CreateTempSubdirectory(name).FullName);
        await File.WriteAllBytesAsync(running.ModulePath, module).ConfigureAwait(false);
        await File.WriteAllBytesAsync(running.HostPath, host).ConfigureAwait(false);

        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = running.WorkingDirectory
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(directory, "Pragmatic.Composition.HostWiring.Tests.runtimeconfig.json"));
        if (rollForwardToLatestMajor)
        {
            start.ArgumentList.Add("--roll-forward");
            start.ArgumentList.Add("LatestMajor");
        }

        start.ArgumentList.Add(running.HostPath);
        start.ArgumentList.Add("--urls=http://127.0.0.1:0");
        foreach (var arg in hostArgs)
            start.ArgumentList.Add(arg);
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        running.Process = Process.Start(start)!;
        return running;
    }

    public async Task StopAsync()
    {
        if (!Process.HasExited)
            Process.Kill(entireProcessTree: true);
        await Process.WaitForExitAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Process is not null)
        {
            await StopAsync().ConfigureAwait(false);
            Process.Dispose();
        }

        File.Delete(HostPath);
        File.Delete(ModulePath);
        Directory.Delete(WorkingDirectory, recursive: true);
    }
}
