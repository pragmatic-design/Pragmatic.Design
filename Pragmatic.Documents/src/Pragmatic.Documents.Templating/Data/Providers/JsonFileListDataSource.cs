using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Documents.Templating.Data.Providers;

/// <summary>
/// Data source that reads a JSON file as a list of objects.
/// </summary>
public sealed class JsonFileListDataSource<T> : IDataSourceProvider where T : class
{
    private readonly string _filePath;
    private readonly JsonTypeInfo<List<T>>? _typeInfo;
    private readonly JsonSerializerOptions? _options;

    public string Name { get; }
    public Type ValueType => typeof(List<T>);

    /// <summary>AOT-safe constructor with JsonTypeInfo.</summary>
    /// <remarks><c>allowedRoot</c>: optional directory the file must reside under; rejects path traversal outside it.</remarks>
    public JsonFileListDataSource(string name, string filePath, JsonTypeInfo<List<T>> typeInfo, string? allowedRoot = null)
    {
        Name = name;
        _filePath = JsonFilePath.Normalize(filePath, allowedRoot);
        _typeInfo = typeInfo;
    }

    /// <summary>Convenience constructor (non-AOT).</summary>
    /// <remarks><c>allowedRoot</c>: optional directory the file must reside under; rejects path traversal outside it.</remarks>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT.")]
    public JsonFileListDataSource(string name, string filePath, JsonSerializerOptions? options = null, string? allowedRoot = null)
    {
        Name = name;
        _filePath = JsonFilePath.Normalize(filePath, allowedRoot);
        _options = options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by _typeInfo null check; non-AOT path is opt-in.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Guarded by _typeInfo null check; non-AOT path is opt-in.")]
    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        await using var stream = JsonFilePath.OpenRead(_filePath);
        return _typeInfo is not null
            ? await JsonSerializer.DeserializeAsync(stream, _typeInfo, ct)
            : await JsonSerializer.DeserializeAsync<List<T>>(stream, _options, ct);
    }
}
