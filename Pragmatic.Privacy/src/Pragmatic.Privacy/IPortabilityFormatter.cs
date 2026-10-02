namespace Pragmatic.Privacy;

/// <summary>
///     Renders an export in a form the subject can take elsewhere.
/// </summary>
/// <remarks>
///     Portability means the subject can hand the file to someone else and have it be usable — so the
///     format has to be structured and commonly readable, not a PDF that looks tidy and can only be
///     retyped. The format is pluggable because "commonly readable" depends on the sector.
/// </remarks>
public interface IPortabilityFormatter
{
    /// <summary>The media type the output should be served as.</summary>
    string ContentType { get; }

    /// <summary>A file extension, without the dot.</summary>
    string FileExtension { get; }

    /// <summary>Renders the export.</summary>
    ValueTask<byte[]> FormatAsync(SubjectDataExport export, CancellationToken ct = default);
}
