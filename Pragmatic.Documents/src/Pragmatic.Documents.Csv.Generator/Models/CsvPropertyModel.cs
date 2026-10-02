namespace Pragmatic.Documents.Csv.Generator.Models;

/// <summary>A single property in a CSV-serializable type.</summary>
/// <remarks>
///     <c>IsWritable</c>: the property has a setter or <c>init</c> the generated code can reach — it is
///     nested in the type, so any accessibility does. A computed (get-only) property is written as a
///     column and not read back.
/// </remarks>
internal sealed record CsvPropertyModel(
    string PropertyName,
    string TypeName,
    string Header,
    string? Format,
    int Order,
    bool IsNullable,
    CsvPropertyKind Kind,
    bool IsWritable);

/// <summary>Category of property for serialization code generation.</summary>
internal enum CsvPropertyKind
{
    String,
    Int,
    Long,
    Double,
    Float,
    Decimal,
    DateTime,
    DateTimeOffset,
    Bool,
    Enum,
    Guid,
    TimeSpan,
    Other
}
