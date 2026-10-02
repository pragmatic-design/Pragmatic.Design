using System.Text.Json.Serialization;
using Pragmatic.Documents.Templating;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>
/// Base email template node — parallel to <see cref="Model.EmailNode"/> with expression support.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(EmailTextTemplate), "text")]
[JsonDerivedType(typeof(EmailHeadingTemplate), "heading")]
[JsonDerivedType(typeof(EmailImageTemplate), "image")]
[JsonDerivedType(typeof(EmailButtonTemplate), "button")]
[JsonDerivedType(typeof(EmailSpacerTemplate), "spacer")]
[JsonDerivedType(typeof(EmailDividerTemplate), "divider")]
[JsonDerivedType(typeof(EmailHtmlTemplate), "html")]
[JsonDerivedType(typeof(EmailTableTemplate), "table")]
[JsonDerivedType(typeof(EmailPartialTemplate), "partial")]
public abstract record EmailNodeTemplate
{
    public TemplateDirectives? Directives { get; init; }
}
