using System.ComponentModel;

namespace Pragmatic.Logging.CallSites;

/// <summary>The writer a generated state of type <typeparamref name="TState" /> registered, if any.</summary>
/// <remarks>
///     Set by the generated state's type initializer, which runs before the call site first touches the
///     type; read by a provider for each call. A type parameter that is not a generated state has no
///     writer, and the provider takes its usual path.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Utf8LogStateWriters<TState>
{
    /// <summary>The writer, or null when <typeparamref name="TState" /> is not a generated call site's state.</summary>
    public static IUtf8LogStateWriter<TState>? Writer { get; set; }
}
