namespace Pragmatic.Logging.Context.Providers;

/// <summary>
/// Provides thread-level context information.
/// </summary>
/// <remarks>
///     Not static: the properties describe the thread that is logging, so they are read on every call.
/// </remarks>
public sealed class ThreadContextProvider() : ContextProviderBase("Thread", priority: 500), IPerCallContextWriter
{
    /// <inheritdoc />
    public override bool IsStatic => false;

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

    // In the order of the bits WriteContextProperties reads.
    private static readonly string[] Names =
        ["ThreadId", "ThreadName", "IsBackground", "IsThreadPoolThread", "CurrentCulture", "CurrentUICulture"];

    IReadOnlyList<string> IPerCallContextWriter.PropertyNames => Names;

    void IPerCallContextWriter.WriteContextProperties(IDictionary<string, object?> target, ulong included)
    {
        var currentThread = Thread.CurrentThread;

        if ((included & 1UL << 0) != 0)
            target["ThreadId"] = currentThread.ManagedThreadId;
        if ((included & 1UL << 1) != 0 && currentThread.Name is { } name)
            target["ThreadName"] = name;
        if ((included & 1UL << 2) != 0)
            target["IsBackground"] = currentThread.IsBackground;
        if ((included & 1UL << 3) != 0)
            target["IsThreadPoolThread"] = currentThread.IsThreadPoolThread;
        if ((included & 1UL << 4) != 0)
            target["CurrentCulture"] = currentThread.CurrentCulture.Name;
        if ((included & 1UL << 5) != 0)
            target["CurrentUICulture"] = currentThread.CurrentUICulture.Name;
    }
}
