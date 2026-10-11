using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks.Modules;

/// <summary>
///     The same two methods of <c>ActionInvokerBase</c>, generated as Pragmatic call sites, as Pragmatic.Actions
///     declares them with <c>PragmaticLogCallSites</c>.
/// </summary>
internal sealed partial class ActionsCallSiteLog(ILogger logger)
{
    public const string StartTemplate = "{kind} {actionName} starting";
    public const string SuccessTemplate = "{kind} {actionName} succeeded in {elapsedMs}ms";

    private readonly ILogger _logger = logger;

    [LoggerMessage(Level = LogLevel.Debug, Message = StartTemplate)]
    public partial void LogStart(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Information, Message = SuccessTemplate)]
    public partial void LogSuccess(string kind, string actionName, long elapsedMs);
}
