using System.Collections.Concurrent;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// A minimal in-memory <see cref="IFileStorage"/> used by the DI / builder samples to demonstrate
/// the generic <c>AddFileStorage&lt;T&gt;()</c> and <c>UseStorage&lt;T&gt;()</c> registrations:
/// it has a parameterless constructor, so the DI container can activate it directly (unlike
/// <c>LocalDiskFileStorage</c>, which requires a base path). Not part of the shipped library.
/// </summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var key = $"{container}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        _files[key] = buffer.ToArray();
        return new Uri($"mem://{key}", UriKind.Absolute);
    }

    public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.TryGetValue(Key(fileUri), out var bytes)
            ? (Stream)new MemoryStream(bytes, writable: false)
            : null);

    public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.ContainsKey(Key(fileUri)));

    public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        _files.TryRemove(Key(fileUri), out _);
        return Task.CompletedTask;
    }

    private static string Key(Uri fileUri) => $"{fileUri.Host}{fileUri.AbsolutePath}";
}
