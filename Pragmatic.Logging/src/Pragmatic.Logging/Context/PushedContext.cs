namespace Pragmatic.Logging.Context;

/// <summary>
///     A node of <see cref="AmbientChain" /> for a context pushed with <see cref="LogContextScope.Push" />: the same
///     context may be a node in another flow, so its own link is not rewritten.
/// </summary>
internal sealed class PushedContext(LogContext context, object? previous)
{
    public LogContext Context { get; } = context;

    public object? Previous { get; } = previous;
}
