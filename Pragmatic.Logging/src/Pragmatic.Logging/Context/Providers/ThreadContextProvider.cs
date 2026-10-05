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

    void IPerCallContextWriter.WriteContextProperties(IDictionary<string, object?> target, Func<string, bool> include)
    {
        var currentThread = Thread.CurrentThread;

        if (include("ThreadId"))
            target["ThreadId"] = currentThread.ManagedThreadId;
        if (currentThread.Name is { } name && include("ThreadName"))
            target["ThreadName"] = name;
        if (include("IsBackground"))
            target["IsBackground"] = currentThread.IsBackground;
        if (include("IsThreadPoolThread"))
            target["IsThreadPoolThread"] = currentThread.IsThreadPoolThread;
        if (include("CurrentCulture"))
            target["CurrentCulture"] = currentThread.CurrentCulture.Name;
        if (include("CurrentUICulture"))
            target["CurrentUICulture"] = currentThread.CurrentUICulture.Name;
    }
}
