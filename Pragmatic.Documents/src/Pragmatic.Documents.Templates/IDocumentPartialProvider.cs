namespace Pragmatic.Documents.Templates;

/// <summary>
/// Provides partial templates by name (from DB, file system, or in-memory).
/// </summary>
public interface IDocumentPartialProvider
{
    ValueTask<DocumentPartialTemplate?> GetAsync(string name, CancellationToken ct = default);
}
