namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// A data source backed by an async factory. Resolved once and cached.
/// </summary>
public sealed class AsyncDataSource<T>(string name, Func<CancellationToken, ValueTask<T>> factory) : IDataSourceProvider
    where T : class
{
    private readonly Lock _gate = new();
    private Task<T>? _resolution;

    public string Name { get; } = name;
    public Type ValueType { get; } = typeof(T);

    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        // Memoize the factory invocation: started once, awaited by all callers (thread-safe).
        Task<T> resolution;
        lock (_gate)
        {
            resolution = _resolution ??= factory(ct).AsTask();
        }
        return await resolution;
    }
}
