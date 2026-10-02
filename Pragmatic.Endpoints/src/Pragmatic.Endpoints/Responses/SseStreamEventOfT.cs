using Pragmatic.Result;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Envelope produced by the generated SSE adapter: either a stream item or a failure.
///     The generated handler maps each <c>Result</c> from the streaming endpoint into one
///     of these; the SSE writer turns items into <c>data:</c> events and errors into a
///     terminal <c>event: error</c>.
/// </summary>
/// <typeparam name="TItem">The stream item type.</typeparam>
public readonly struct SseStreamEvent<TItem>
{
    private SseStreamEvent(TItem? item, IError? error)
    {
        Item = item;
        Error = error;
    }

    /// <summary>The stream item; default when <see cref="IsError" />.</summary>
    public TItem? Item { get; }

    /// <summary>The failure; null when the event carries an item.</summary>
    public IError? Error { get; }

    /// <summary>Whether this event carries a failure.</summary>
    public bool IsError => Error is not null;

    /// <summary>Wraps a stream item.</summary>
    public static SseStreamEvent<TItem> FromItem(TItem item) => new(item, null);

    /// <summary>Wraps a failure.</summary>
    public static SseStreamEvent<TItem> FromError(IError error) => new(default, error);
}
