using System.Xml.Linq;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Refuses what a markup does not know: an element or an attribute nobody reads.
/// </summary>
/// <remarks>
///     Both parsers refuse rather than drop them. Dropped, an unknown element becomes nothing and an
///     unknown attribute is never read, so a template written from an old page of the docs —
///     <c>&lt;section&gt;</c>, a misspelt <c>data-source</c> — renders empty or unbound and says nothing.
///     A refusal with the name of what was written is the only report a static parser can give: there
///     is nobody to warn.
/// </remarks>
internal static class MarkupVocabulary
{
    /// <summary>The directives every content node reads.</summary>
    public static readonly string[] Directives = ["if", "for"];

    /// <summary>Throws when <paramref name="element" /> carries an attribute outside <paramref name="known" />.</summary>
    public static void RequireKnownAttributes(XElement element, params string[] known)
    {
        foreach (var attribute in element.Attributes())
        {
            // Namespace declarations (xmlns, xmlns:x) are the reader's business, not the markup's.
            if (attribute.IsNamespaceDeclaration)
                continue;

            var name = attribute.Name.LocalName;
            if (Array.IndexOf(known, name) < 0)
            {
                throw new MarkupParseException(
                    $"Unknown attribute '{name}' on <{element.Name.LocalName}>. "
                    + (known.Length == 0 ? "It takes none." : $"Known: {string.Join(", ", known)}."));
            }
        }
    }

    /// <summary>The refusal for an element the parent does not take.</summary>
    public static MarkupParseException UnknownElement(XElement element, string where)
        => new($"Unknown element <{element.Name.LocalName}> in {where}.");
}
