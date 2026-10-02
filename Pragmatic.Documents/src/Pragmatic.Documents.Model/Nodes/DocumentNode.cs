using System.Text.Json.Serialization;

namespace Pragmatic.Documents.Model;

/// <summary>
/// Base node for all document content elements.
/// Uses JSON $type discriminator for polymorphic (de)serialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(HeadingNode), "heading")]
[JsonDerivedType(typeof(ParagraphNode), "paragraph")]
[JsonDerivedType(typeof(ImageNode), "image")]
[JsonDerivedType(typeof(TableNode), "table")]
[JsonDerivedType(typeof(ListNode), "list")]
[JsonDerivedType(typeof(HorizontalRuleNode), "hr")]
[JsonDerivedType(typeof(SpacerNode), "spacer")]
[JsonDerivedType(typeof(ContainerNode), "container")]
[JsonDerivedType(typeof(PageBreakNode), "pagebreak")]
[JsonDerivedType(typeof(BarcodeNode), "barcode")]
[JsonDerivedType(typeof(HyperlinkNode), "hyperlink")]
[JsonDerivedType(typeof(TocNode), "toc")]
[JsonDerivedType(typeof(FootnoteNode), "footnote")]
[JsonDerivedType(typeof(FieldNode), "field")]
[JsonDerivedType(typeof(BookmarkNode), "bookmark")]
public abstract record DocumentNode
{
    /// <summary>Optional CSS-like style properties.</summary>
    public NodeStyle? Style { get; init; }
}
