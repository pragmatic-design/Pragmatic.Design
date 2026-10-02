using System.Text;
using System.Text.Json;

namespace Pragmatic.Privacy;

/// <summary>
///     Default <see cref="IPortabilityFormatter" />: JSON, indented so a person can read it and a
///     machine can parse it.
/// </summary>
public sealed class JsonPortabilityFormatter : IPortabilityFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // A subject's data routinely contains accented characters and non-Latin scripts. Escaping them
        // to \uXXXX produces a file that is technically correct and unreadable to the person it is for.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public string FileExtension => "json";

    /// <inheritdoc />
    public ValueTask<byte[]> FormatAsync(SubjectDataExport export, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(export);

        var document = new
        {
            subject = export.SubjectRef,
            generatedAt = export.GeneratedAt,
            data = export.Categories,
            consents = export.Consents.Select(c => new
            {
                c.Purpose,
                c.NoticeVersion,
                c.GrantedAt,
                c.WithdrawnAt,
                c.Source,
                active = c.IsActive
            })
        };

        return ValueTask.FromResult(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, Options)));
    }
}
