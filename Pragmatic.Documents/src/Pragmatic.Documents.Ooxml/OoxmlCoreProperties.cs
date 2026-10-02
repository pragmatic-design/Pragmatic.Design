namespace Pragmatic.Documents.Ooxml;

/// <summary>
/// Dublin Core metadata for OOXML documents (docProps/core.xml).
/// Shared between DOCX and XLSX.
/// </summary>
public sealed record OoxmlCoreProperties
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Keywords { get; init; }
    public string? Language { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
}
