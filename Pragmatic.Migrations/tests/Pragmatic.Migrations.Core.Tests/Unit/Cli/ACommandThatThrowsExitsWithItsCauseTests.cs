using Pragmatic.Migrations.Cli;
using Pragmatic.Testing.Assertions;
using Spectre.Console;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     A command that throws ends the CLI with exit code 1 and the exception's type and message on the
///     console — not with Spectre's own <c>-1</c>.
/// </summary>
/// <remarks>
///     Time off's build runs <c>snapshot</c> after every build, and in the gate it failed at
///     random with <c>MSB3073 … exit code -1</c>. The CLI returns 1 on every path it handles; -1 is what
///     <c>CommandApp</c> answers when a command throws and exceptions are not propagated, and the
///     rendering it writes instead went to a console the quiet build never shows. Propagated, the
///     exception reaches <c>CliApp</c>'s own handler, which says what it was.
/// </remarks>
public class ACommandThatThrowsExitsWithItsCauseTests
{
    [Fact]
    public async Task ASnapshotOfAMissingAssembly_Exits1_NamingTheException()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        var output = new StringWriter();
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output) });
        try
        {
            var exit = await CliApp.RunAsync(["snapshot", "--assembly", missing, "--output", Path.GetTempPath()]);

            exit.Should().Be(1, $"a handled failure is 1; the console said:\n{output.ToString()}");
            output.ToString().Should().Contain("FileNotFoundException");
            output.ToString().Should().Contain(Path.GetFileName(missing));
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }
}
