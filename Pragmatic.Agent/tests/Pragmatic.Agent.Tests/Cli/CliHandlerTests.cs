using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Cli;
using Xunit;

namespace Pragmatic.Agent.Tests.Cli;

/// <summary>
///     Unit coverage for the parts of <see cref="CliHandler.ExecuteAsync"/> that run before any
///     socket connection is attempted. The connect/command paths require a live Agent daemon
///     socket and are out of scope for pure-unit tests, so only the no-args usage path (which
///     prints help and returns a non-zero exit code without connecting) is exercised here.
/// </summary>
public class CliHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_NoArgs_PrintsUsageAndReturnsNonZero()
    {
        var exit = await CliHandler.ExecuteAsync([]);

        exit.Should().Be(1);
    }
}
