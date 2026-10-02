namespace Pragmatic.Documents.Model;

/// <summary>
/// Dynamic field that is evaluated at render time (page number, date, etc.).
/// </summary>
public sealed record FieldNode : DocumentNode
{
    /// <summary>The type of field.</summary>
    public required FieldType FieldType { get; init; }

    /// <summary>Optional format string (e.g. date format).</summary>
    public string? Format { get; init; }
}
