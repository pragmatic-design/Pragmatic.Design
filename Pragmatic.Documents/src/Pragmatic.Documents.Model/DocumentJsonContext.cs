using System.Text.Json.Serialization;

namespace Pragmatic.Documents.Model;

/// <summary>
/// Source-generated JSON serializer context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(DocumentModel))]
[JsonSerializable(typeof(DocumentPage))]
[JsonSerializable(typeof(DocumentNode))]
[JsonSerializable(typeof(TextNode))]
[JsonSerializable(typeof(HeadingNode))]
[JsonSerializable(typeof(ParagraphNode))]
[JsonSerializable(typeof(ImageNode))]
[JsonSerializable(typeof(TableNode))]
[JsonSerializable(typeof(ListNode))]
[JsonSerializable(typeof(HorizontalRuleNode))]
[JsonSerializable(typeof(SpacerNode))]
[JsonSerializable(typeof(ContainerNode))]
[JsonSerializable(typeof(PageBreakNode))]
[JsonSerializable(typeof(BarcodeNode))]
[JsonSerializable(typeof(HyperlinkNode))]
[JsonSerializable(typeof(TocNode))]
[JsonSerializable(typeof(FootnoteNode))]
[JsonSerializable(typeof(FieldNode))]
[JsonSerializable(typeof(BookmarkNode))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class DocumentJsonContext : JsonSerializerContext;
