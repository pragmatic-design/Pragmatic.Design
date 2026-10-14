using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
///     The one ambient value of logging (#134): the top node of a chain that holds both the pushed contexts and the
///     logger's scopes. With an ambient value each, every push and pop under the other copied a two-entry map.
/// </summary>
/// <remarks>
///     A node is a <see cref="LogContext" /> pushed as it was created, a <see cref="PushedContext" /> for a context
///     pushed again from elsewhere, or an <see cref="AmbientScope" />. A node never changes once it is the top, so a
///     flow that captured the chain reads it safely. The nearest context and the nearest scope are found by walking
///     down, which in a request is a node or two.
/// </remarks>
internal static class AmbientChain
{
    private static readonly AsyncLocal<object?> _top = new();

    public static object? Current
    {
        get => _top.Value;
        set => _top.Value = value;
    }

    public static LogContext? NearestContext(object? node)
    {
        while (node != null)
        {
            switch (node)
            {
                case LogContext context:
                    return context;
                case PushedContext pushed:
                    return pushed.Context;
                default:
                    node = ((AmbientScope)node).Previous;
                    break;
            }
        }

        return null;
    }

    public static AmbientScope? NearestScope(object? node)
    {
        while (node != null)
        {
            switch (node)
            {
                case AmbientScope scope:
                    return scope;
                case LogContext context:
                    node = context.AmbientPrevious;
                    break;
                default:
                    node = ((PushedContext)node).Previous;
                    break;
            }
        }

        return null;
    }

    /// <summary>
    ///     Makes <paramref name="context" />, created for this push and seen by nobody else, the top node.
    /// </summary>
    /// <returns>What pops it.</returns>
    // Inlined into the push so the AmbientRestore is allocated where the caller's using holds it: without the
    // hint the JIT left it a call away, and it reached the heap (32 B per push in ClassicProbeBenchmarks).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDisposable PushNew(LogContext context)
    {
        var previous = _top.Value;
        context.AmbientPrevious = previous;
        _top.Value = context;
        return new AmbientRestore(previous);
    }

    /// <summary>Pushes a context that may be a node elsewhere already, wrapped so its own link stays untouched.</summary>
    public static IDisposable PushExisting(LogContext context)
    {
        var previous = _top.Value;
        _top.Value = new PushedContext(context, previous);
        return new AmbientRestore(previous);
    }
}
