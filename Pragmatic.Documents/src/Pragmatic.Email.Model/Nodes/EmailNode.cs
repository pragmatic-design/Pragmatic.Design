using System.Text.Json.Serialization;

namespace Pragmatic.Email.Model;

/// <summary>
/// Base node for all email content elements.
/// Uses JSON $type discriminator for polymorphic (de)serialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(EmailTextNode), "text")]
[JsonDerivedType(typeof(EmailHeadingNode), "heading")]
[JsonDerivedType(typeof(EmailImageNode), "image")]
[JsonDerivedType(typeof(EmailButtonNode), "button")]
[JsonDerivedType(typeof(EmailSpacerNode), "spacer")]
[JsonDerivedType(typeof(EmailDividerNode), "divider")]
[JsonDerivedType(typeof(EmailHtmlNode), "html")]
[JsonDerivedType(typeof(EmailTableNode), "table")]
public abstract record EmailNode;
