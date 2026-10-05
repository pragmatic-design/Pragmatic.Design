using Microsoft.Extensions.Logging;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Bootstrap;

/// <summary>A bootstrap line names its level.</summary>
/// <remarks>
///     The bootstrap template used Serilog's <c>{Level:u3}</c>. The console provider replaces only the
///     literal <c>{Level}</c> token, so every bootstrap line carried the placeholder instead of the level.
/// </remarks>
[Collection(ConsoleOutputCollection.Name)]
public class BootstrapLoggerWritesTheLevelTests
{
    [Fact]
    public void ABootstrapLine_NamesItsLevel_AndCarriesNoPlaceholder()
    {
        using var factory = BootstrapLogger.CreateBootstrapLoggerFactory();
        var logger = factory.CreateLogger("Bootstrap.Test");

        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            logger.LogInformation("Starting");

            var output = consoleCapture.ToString();
            output.Should().Contain("INFO").And.Contain("Starting");
            output.Should().NotContain("{Level");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
