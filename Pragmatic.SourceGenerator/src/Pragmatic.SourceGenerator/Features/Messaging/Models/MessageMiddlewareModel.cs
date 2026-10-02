namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     A class marked <c>[MessageMiddleware]</c>, and the message type it scoped itself to.
/// </summary>
/// <remarks>
///     ⚠️ Without a model the declarative path would register nothing and the middleware would never
///     be called. Nothing would fail — the messages would be handled, only not wrapped.
/// </remarks>
internal sealed record MessageMiddlewareModel
{
    /// <summary>FQN of the middleware class, with the <c>global::</c> prefix.</summary>
    public required string TypeFqn { get; init; }

    /// <summary>
    ///     Whether this class can actually be registered: concrete, and implementing the interface.
    /// </summary>
    /// <remarks>
    ///     A fact rather than a rejection. One that cannot is reported by the shape diagnostic
    ///     (PRAG0803); saying so twice would report one mistake twice, and saying so nowhere is what
    ///     the silent-drop ratchet counts.
    /// </remarks>
    public bool CanServe { get; init; }

    /// <summary>
    ///     FQN of the one message type it applies to, or null when it applies to every message.
    /// </summary>
    public string? ForMessageTypeFqn { get; init; }
}
