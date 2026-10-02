using Microsoft.Extensions.Logging;

Console.WriteLine("=== Pragmatic.Logging.Consumer (PackageReference) ===");

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole(opts =>
    {
        opts.SingleLine = true;
        opts.TimestampFormat = "HH:mm:ss ";
    }));

var logger = loggerFactory.CreateLogger("Demo");

// Plain structured logging — proves the runtime package is wired up.
logger.LogInformation("Hello {Who} from {Host}", "consumer", Environment.MachineName);

// High-performance logging via Microsoft's built-in [LoggerMessage] source generator
// (shipped with the SDK). This is the recommended hot-path pattern with Pragmatic.Logging.
var demo = new DemoLogs();
demo.Greet(logger, "acme");
demo.Counted(logger, 42);

Console.WriteLine("=== done ===");

internal sealed partial class DemoLogs
{
    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "Source-generated greet for {Tenant}")]
    public partial void Greet(ILogger logger, string tenant);

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Debug,
        Message = "Source-generated counter reached {Count}")]
    public partial void Counted(ILogger logger, int count);
}
