using System.Text.Json.Serialization;

namespace Pragmatic.Email.Model;

/// <summary>
/// Source-generated JSON serializer context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(EmailModel))]
[JsonSerializable(typeof(EmailSection))]
[JsonSerializable(typeof(EmailColumn))]
[JsonSerializable(typeof(EmailNode))]
[JsonSerializable(typeof(EmailTextNode))]
[JsonSerializable(typeof(EmailHeadingNode))]
[JsonSerializable(typeof(EmailImageNode))]
[JsonSerializable(typeof(EmailButtonNode))]
[JsonSerializable(typeof(EmailSpacerNode))]
[JsonSerializable(typeof(EmailDividerNode))]
[JsonSerializable(typeof(EmailHtmlNode))]
[JsonSerializable(typeof(EmailTableNode))]
[JsonSerializable(typeof(EmailTableColumn))]
[JsonSerializable(typeof(EmailTableRow))]
[JsonSerializable(typeof(EmailTableCell))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class EmailJsonContext : JsonSerializerContext;
