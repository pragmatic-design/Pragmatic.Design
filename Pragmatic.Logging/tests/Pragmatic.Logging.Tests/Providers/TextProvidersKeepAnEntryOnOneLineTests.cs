using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A provider that writes text writes one entry as one line, whatever its values hold. A value carrying CR or LF
///     (a decoded request path, a header) otherwise ends the entry where the caller chose and starts a line the caller
///     wrote, which a reader of the log takes for an entry of its own. The value is still there, escaped.
/// </summary>
/// <remarks>
///     The value reaches both the rendered message and the structured properties, so each test covers both: escaping
///     one and not the other leaves the forged line in place.
/// </remarks>
[Collection(ConsoleOutputCollection.Name)]
public sealed class TextProvidersKeepAnEntryOnOneLineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"PragmaticLogging_OneLine_{Guid.NewGuid():N}");
    private readonly string _marker = Guid.NewGuid().ToString("N");

    public TextProvidersKeepAnEntryOnOneLineTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string Forged => $"/{_marker}\r\n[00:00:00.000] [CRIT] Forged: FAKE line\u0007";

    private string Escaped => $"/{_marker}\\r\\n[00:00:00.000] [CRIT] Forged: FAKE line\\^G";

    [Fact]
    public void Console_AValueWithCrLf_WritesOneLine()
    {
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.IncludeStructuredProperties = true;
        using var provider = new PragmaticConsoleProvider("OneLine", config);
        var logger = provider.CreateLogger("Test.Category");

        using var capture = new StringWriter();
        var original = Console.Out;
        Console.SetOut(capture);
        try
        {
            logger.LogInformation("Request for {Path}", Forged);
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = Lines(capture.ToString());
        lines.Should().ContainSingle();
        lines[0].Should().Contain($"Request for {Escaped}").And.Contain($"Path=\"{Escaped}\"");
    }

    [Fact]
    public void Debug_AValueWithCrLf_WritesOneLine()
    {
        var config = PragmaticDebugConfiguration.ForDebug();
        config.IncludeStructuredProperties = true;
        using var provider = new PragmaticDebugProvider("OneLine", config);
        var logger = provider.CreateLogger("Test.Category");

        var captured = new List<string>();
        var listener = new TestTraceListener(captured);
        Trace.Listeners.Add(listener);
        try
        {
            logger.LogInformation("Request for {Path}", Forged);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        // Trace is process-wide: another test may write to it meanwhile, so only this entry is read.
        string entry;
        lock (captured)
            entry = captured.Single(text => text.Contains(_marker, StringComparison.Ordinal));

        var lines = Lines(entry);
        lines.Should().ContainSingle();
        lines[0].Should().Contain($"Request for {Escaped}").And.Contain($"Path=\"{Escaped}\"");
    }

    [Fact]
    public void File_AValueWithCrLf_WritesOneLine()
    {
        var path = Path.Combine(_directory, "one-line.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.IncludeStructuredProperties = true;
        var provider = new PragmaticFileProvider("OneLine", config, path);
        provider.CreateLogger("Test.Category").LogInformation("Request for {Path}", Forged);

        // Dispose drains the writer's channel and closes the file: the signal that the entry is on disk.
        provider.Dispose();

        var lines = Lines(File.ReadAllText(path));
        lines.Should().ContainSingle();
        lines[0].Should().Contain($"Request for {Escaped}").And.Contain($"Path=\"{Escaped}\"");
    }

    // A bare CR counts as a break as well: a terminal returns to the start of the line on it.
    private static List<string> Lines(string text) =>
        text.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries).ToList();
}
