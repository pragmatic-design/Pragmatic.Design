namespace Pragmatic.Documents.Csv;

/// <summary>
/// Customizes CSV column mapping for a property. Optional — without it, the property name is used as header.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvColumnAttribute : Attribute
{
    /// <summary>Column header name in CSV. If null, property name is used.</summary>
    public string? Header { get; }

    /// <summary>Format string for writing/reading (e.g. "dd/MM/yyyy", "#,##0.00").</summary>
    public string? Format { get; set; }

    /// <summary>Column order (0-based). Default: declaration order.</summary>
    public int Order { get; set; } = -1;

    /// <summary>Whether to skip this property during CSV serialization.</summary>
    public bool Ignore { get; set; }

    public CsvColumnAttribute() { }
    public CsvColumnAttribute(string header) => Header = header;
}
