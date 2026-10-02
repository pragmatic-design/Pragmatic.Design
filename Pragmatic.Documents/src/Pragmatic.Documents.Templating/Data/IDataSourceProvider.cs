namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// A named data source provider that resolves a typed value.
/// Implementations: <see cref="StaticDataSource{T}"/>, <see cref="AsyncDataSource{T}"/>.
/// </summary>
public interface IDataSourceProvider
{
    /// <summary>Unique name used as namespace in templates: <c>{{name.property}}</c>.</summary>
    string Name { get; }

    /// <summary>The CLR type of the resolved value (for IntelliSense/SG integration).</summary>
    Type ValueType { get; }

    /// <summary>Resolve the data source value.</summary>
    ValueTask<object?> ResolveAsync(CancellationToken ct = default);
}
