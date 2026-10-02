namespace Pragmatic.Authoring;

/// <summary>
///     Entry point for marking unimplemented business logic. The generated solution stubs every
///     behavior body with <c>throw Behavior.Pending();</c>; a developer or agent replaces it with the
///     real implementation. The thrown <see cref="PendingBehaviorException"/> is analyzer-discoverable,
///     so "what is still pending?" is a precise query, not a guess.
/// </summary>
public static class Behavior
{
    /// <summary>
    ///     Returns the sentinel exception for a pending behavior, intended to be thrown so it satisfies
    ///     any return type: <c>public X Realize() => throw Behavior.Pending();</c>.
    /// </summary>
    /// <param name="note">Optional context (e.g. the use-case id) surfaced in the message.</param>
    public static PendingBehaviorException Pending(string? note = null) => new(note);
}
