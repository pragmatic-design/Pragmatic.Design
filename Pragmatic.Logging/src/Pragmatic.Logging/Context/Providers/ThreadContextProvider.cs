namespace Pragmatic.Logging.Context.Providers;

/// <summary>
/// Provides thread-level context information.
/// </summary>
public sealed class ThreadContextProvider() : ContextProviderBase("Thread", priority: 500)
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        var currentThread = Thread.CurrentThread;

        return CreatePropertiesDictionary(
            ("ThreadId", currentThread.ManagedThreadId),
            ("ThreadName", currentThread.Name),
            ("IsBackground", currentThread.IsBackground),
            ("IsThreadPoolThread", currentThread.IsThreadPoolThread),
            ("CurrentCulture", currentThread.CurrentCulture.Name),
            ("CurrentUICulture", currentThread.CurrentUICulture.Name)
        );
    }
}