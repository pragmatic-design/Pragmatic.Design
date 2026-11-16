using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     An application that configures Pragmatic's console writes each entry to the console once, through the provider that keeps it
///     on one line. <c>WebApplication.CreateBuilder</c> registers Microsoft's console provider before the application configures
///     anything, and the composed factory kept it beside Pragmatic's. Every entry then reached the console twice, the second time
///     unescaped, and a decoded request path carrying CR/LF forged a line there (CodeQL <c>cs/log-forging</c>).
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class TheConsoleIsWrittenOnceTests
{
    private readonly string _marker = Guid.NewGuid().ToString("N");

    private string Forged => $"/{_marker}\r\nFORGED-{_marker}";

    [Fact]
    public void WithPragmaticsConsole_AnEntryReachesTheConsoleOnce_OnOneLine()
    {
        var lines = WhatTheConsoleShows(pragmatic => pragmatic.AddConsole(PragmaticConsoleConfiguration.ForAdvancedConsole()));

        lines.Where(line => line.Contains(_marker, StringComparison.Ordinal)).Should().ContainSingle(
            "one console provider writes the entry, and it keeps it on one line");
        lines.Should().NotContain(line => line.StartsWith($"FORGED-{_marker}", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The control: with no console of Pragmatic's, Microsoft's stays. An application that configured only a file does not lose its
    ///     console output.
    /// </summary>
    [Fact]
    public void WithoutPragmaticsConsole_MicrosoftsConsoleStillWrites()
    {
        var lines = WhatTheConsoleShows(_ => { });

        lines.Should().Contain(line => line.Contains(_marker, StringComparison.Ordinal));
    }

    // A host's logging: Microsoft's console first, as WebApplication.CreateBuilder registers it, then Pragmatic.Logging.
    private List<string> WhatTheConsoleShows(Action<PragmaticLoggingBuilder> configure)
    {
        using var capture = new StringWriter();
        var original = Console.Out;
        Console.SetOut(capture);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddConsole());
            services.AddPragmaticLogging(configure);

            // Disposing the container drains Microsoft's console queue, which it writes from a thread of its own.
            using (var provider = services.BuildServiceProvider())
                provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test.Category")
                    .LogWarning("Tenant not resolved for {Path}", Forged);
        }
        finally
        {
            Console.SetOut(original);
        }

        return capture.ToString().Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
