namespace Pragmatic.Authoring;

/// <summary>
///     Sentinel thrown from a not-yet-implemented business-behavior body. A dedicated subtype of
///     <see cref="NotImplementedException"/> so analyzers and the completeness critic can find every
///     pending behavior precisely, without matching plain <c>NotImplementedException</c>.
/// </summary>
public sealed class PendingBehaviorException(string? note = null)
    : NotImplementedException(note is null
        ? "This business behavior is pending implementation."
        : $"Pending business behavior: {note}");
