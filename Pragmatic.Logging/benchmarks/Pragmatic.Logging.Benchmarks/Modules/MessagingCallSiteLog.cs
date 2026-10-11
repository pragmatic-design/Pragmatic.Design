using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks.Modules;

/// <summary>
///     Two of the message bus's methods, generated as Pragmatic call sites: <c>LogPublishing</c> of
///     <c>TransportAwareMessageBus</c>, called on every publish, and <c>LogHandlerFailed</c> of
///     <c>InMemoryMessageBus</c>, called on every failed handler.
/// </summary>
internal sealed partial class MessagingCallSiteLog(ILogger logger)
{
    public const string PublishingTemplate = "Publishing {MessageType} to topic {Topic} via {Transport} (id: {MessageId})";
    public const string HandlerFailedTemplate = "Handler {HandlerName} failed for message {MessageName}";

    private readonly ILogger _logger = logger;

    [LoggerMessage(Level = LogLevel.Debug, Message = PublishingTemplate)]
    public partial void LogPublishing(string messageType, string topic, string transport, string messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = HandlerFailedTemplate)]
    public partial void LogHandlerFailed(string messageName, string handlerName, Exception ex);
}
