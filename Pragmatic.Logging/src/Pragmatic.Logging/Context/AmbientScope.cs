namespace Pragmatic.Logging.Context;

/// <summary>
///     A logger scope as a node of <see cref="AmbientChain" />. The node is the caller's <see cref="IDisposable" />
///     too: it is the ambient value, so it reaches the heap either way, and one object is one allocation.
/// </summary>
internal sealed class AmbientScope : IDisposable
{
    private bool _disposed;

    public AmbientScope(object state)
    {
        State = state;
        Previous = AmbientChain.Current;
        AmbientChain.Current = this;
    }

    public object State { get; }

    public object? Previous { get; }

    /// <summary>The scope this one was begun under.</summary>
    public AmbientScope? Outer => AmbientChain.NearestScope(Previous);

    public void Dispose()
    {
        if (_disposed)
            return;

        AmbientChain.Current = Previous;
        _disposed = true;
    }
}
