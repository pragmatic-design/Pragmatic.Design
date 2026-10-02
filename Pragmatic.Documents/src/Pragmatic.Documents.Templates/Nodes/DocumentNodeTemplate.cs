using System.Text.Json.Serialization;
using Pragmatic.Documents.Templating;

namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>
/// Base template node — parallel to <see cref="Model.DocumentNode"/> but allows expressions in content
/// and structural directives ($if, $for).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextTemplate), "text")]
[JsonDerivedType(typeof(HeadingTemplate), "heading")]
[JsonDerivedType(typeof(ParagraphTemplate), "paragraph")]
[JsonDerivedType(typeof(ImageTemplate), "image")]
[JsonDerivedType(typeof(TableTemplate), "table")]
[JsonDerivedType(typeof(ListTemplate), "list")]
[JsonDerivedType(typeof(HorizontalRuleTemplate), "hr")]
[JsonDerivedType(typeof(SpacerTemplate), "spacer")]
[JsonDerivedType(typeof(ContainerTemplate), "container")]
[JsonDerivedType(typeof(PageBreakTemplate), "pagebreak")]
[JsonDerivedType(typeof(BarcodeTemplate), "barcode")]
[JsonDerivedType(typeof(ForEachTemplate), "foreach")]
[JsonDerivedType(typeof(PartialTemplate), "partial")]
public abstract record DocumentNodeTemplate
{
    /// <summary>Structural directives ($if, $for).</summary>
    public TemplateDirectives? Directives { get; init; }
}
