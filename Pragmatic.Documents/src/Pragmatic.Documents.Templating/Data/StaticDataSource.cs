namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// A data source backed by a pre-resolved typed value.
/// </summary>
public sealed class StaticDataSource<T>(string name, T value) : IDataSourceProvider, IStaticDataSource where T : class
{
    public string Name { get; } = name;
    public Type ValueType { get; } = typeof(T);

    public ValueTask<object?> ResolveAsync(CancellationToken ct = default)
        => ValueTask.FromResult<object?>(value);

    object? IStaticDataSource.GetValue() => value;
}
