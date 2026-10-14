namespace Pragmatic.Logging.Context;

/// <summary>
///     Pops a pushed context: puts back the top of <see cref="AmbientChain" /> it found.
/// </summary>
/// <remarks>
///     Kept apart from the node on purpose. Held only by the caller's <c>using</c>, it never reaches the ambient
///     value, so the JIT can allocate it on the stack where the push is inlined (#134: making the node its own
///     disposable cost more bytes than the shared chain saved).
/// </remarks>
internal sealed class AmbientRestore(object? previous) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        AmbientChain.Current = previous;
        _disposed = true;
    }
}
