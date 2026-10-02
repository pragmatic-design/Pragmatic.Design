using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Helper methods for endpoint transformation.
/// </summary>
internal static partial class EndpointTransform
{
    private static bool IsFromHeaderAttribute(AttributeData a)
    {
        var fullName = a.AttributeClass?.ToDisplayString();
        var shortName = a.AttributeClass?.Name;

        return fullName == EndpointAttributeNames.FromHeader ||
               fullName == EndpointAttributeNames.AspNetFromHeader ||
               shortName is "FromHeaderAttribute" or "FromHeader" ||
               fullName?.Contains("FromHeader") == true;
    }

    /// <summary>Whether the property carries Pragmatic.Validation's <c>[Required]</c>.</summary>
    private static bool CarriesValidationRequired(IPropertySymbol property)
        => property.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == Pragmatic.SourceGen.AttributeNames.ValidationRequired);

    private static bool HasFromQueryAttribute(IPropertySymbol property)
    {
        return property.GetAttributes()
            .Any(a => IsFromQueryAttribute(a));
    }

    private static bool IsFromQueryAttribute(AttributeData a)
    {
        var fullName = a.AttributeClass?.ToDisplayString();
        var shortName = a.AttributeClass?.Name;

        return fullName == EndpointAttributeNames.FromQuery ||
               fullName == EndpointAttributeNames.AspNetFromQuery ||
               shortName is "FromQueryAttribute" or "FromQuery" ||
               fullName?.Contains("FromQuery") == true;
    }

    private static string GetQueryParameterName(IPropertySymbol property)
    {
        var queryAttr = property.GetAttributes()
            .FirstOrDefault(a => IsFromQueryAttribute(a));

        if (queryAttr is not null)
        {
            // Check constructor argument
            if (queryAttr.ConstructorArguments.Length > 0 && queryAttr.ConstructorArguments[0].Value is string name)
                return name;

            // Check named argument (semantic)
            foreach (var namedArg in queryAttr.NamedArguments)
                if (namedArg is { Key: "Name", Value.Value: string namedName })
                    return namedName;

            // Fallback: parse from syntax when semantic info unavailable
            var syntaxName = GetNamedArgumentFromSyntax(queryAttr, "Name");
            if (!string.IsNullOrEmpty(syntaxName))
                return syntaxName!;
        }

        return ToCamelCase(property.Name);
    }

    /// <summary>
    ///     Extracts a named argument value from attribute syntax when semantic parsing fails.
    /// </summary>
    private static string? GetNamedArgumentFromSyntax(AttributeData attr, string argumentName)
    {
        if (attr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attrSyntax)
            return null;

        if (attrSyntax.ArgumentList is null)
            return null;

        foreach (var arg in attrSyntax.ArgumentList.Arguments)
            // Named argument: Name = "value"
            if (arg.NameEquals?.Name.Identifier.Text == argumentName &&
                arg.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
                return literal.Token.ValueText;

        return null;
    }

    private static bool IsFromClaimAttribute(AttributeData a)
    {
        var fullName = a.AttributeClass?.ToDisplayString();
        var shortName = a.AttributeClass?.Name;

        return fullName == EndpointAttributeNames.FromClaim ||
               shortName is "FromClaimAttribute" or "FromClaim" ||
               fullName?.Contains("FromClaim") == true;
    }

    private static bool IsFromCookieAttribute(AttributeData a)
    {
        var fullName = a.AttributeClass?.ToDisplayString();
        var shortName = a.AttributeClass?.Name;

        return fullName == EndpointAttributeNames.FromCookie ||
               shortName is "FromCookieAttribute" or "FromCookie" ||
               fullName?.Contains("FromCookie") == true;
    }

    private static bool IsFromFormAttribute(AttributeData a)
    {
        var fullName = a.AttributeClass?.ToDisplayString();
        var shortName = a.AttributeClass?.Name;

        return fullName == EndpointAttributeNames.FromForm ||
               fullName == EndpointAttributeNames.AspNetFromForm ||
               shortName is "FromFormAttribute" or "FromForm" ||
               fullName?.Contains("FromForm") == true;
    }

    private static bool HasNonBodyBindingAttribute(IPropertySymbol property)
    {
        // Check for binding attributes that are NOT [FromBody]
        // [FromBody] properties should be included in body properties
        return property.GetAttributes().Any(a =>
        {
            var fullName = a.AttributeClass?.ToDisplayString();
            var shortName = a.AttributeClass?.Name;

            // Check Pragmatic binding attributes
            if (fullName == EndpointAttributeNames.FromRoute ||
                fullName == EndpointAttributeNames.FromQuery ||
                fullName == EndpointAttributeNames.FromHeader ||
                fullName == EndpointAttributeNames.FromClaim ||
                fullName == EndpointAttributeNames.FromCookie ||
                fullName == EndpointAttributeNames.FromForm)
                return true;

            // Check ASP.NET Core binding attributes (backward compatibility)
            if (fullName == EndpointAttributeNames.AspNetFromRoute ||
                fullName == EndpointAttributeNames.AspNetFromQuery ||
                fullName == EndpointAttributeNames.AspNetFromHeader ||
                fullName == EndpointAttributeNames.AspNetFromForm)
                return true;

            // Also check by short name (for cases where type isn't fully resolved)
            if (shortName is "FromRouteAttribute" or "FromRoute" or
                "FromQueryAttribute" or "FromQuery" or
                "FromHeaderAttribute" or "FromHeader" or
                "FromClaimAttribute" or "FromClaim" or
                "FromCookieAttribute" or "FromCookie" or
                "FromFormAttribute" or "FromForm")
                return true;

            // Also check the full display string for partial matches (unresolved types)
            if (fullName?.Contains("FromRoute") == true ||
                fullName?.Contains("FromQuery") == true ||
                fullName?.Contains("FromHeader") == true ||
                fullName?.Contains("FromClaim") == true ||
                fullName?.Contains("FromCookie") == true ||
                fullName?.Contains("FromForm") == true)
                return true;

            return false;
        });
    }

    /// <summary>
    ///     Checks if a property has ANY explicit binding attribute
    ///     ([FromRoute], [FromQuery], [FromHeader], [FromBody], [FromClaim], [FromCookie], [FromForm]).
    /// </summary>
    private static bool HasAnyBindingAttribute(IPropertySymbol property)
    {
        return property.GetAttributes().Any(a =>
        {
            var fullName = a.AttributeClass?.ToDisplayString();
            var shortName = a.AttributeClass?.Name;

            // Check Pragmatic binding attributes
            if (fullName == EndpointAttributeNames.FromRoute ||
                fullName == EndpointAttributeNames.FromQuery ||
                fullName == EndpointAttributeNames.FromHeader ||
                fullName == EndpointAttributeNames.FromBody ||
                fullName == EndpointAttributeNames.FromClaim ||
                fullName == EndpointAttributeNames.FromCookie ||
                fullName == EndpointAttributeNames.FromForm)
                return true;

            // Check ASP.NET Core binding attributes (backward compatibility)
            if (fullName == EndpointAttributeNames.AspNetFromRoute ||
                fullName == EndpointAttributeNames.AspNetFromQuery ||
                fullName == EndpointAttributeNames.AspNetFromHeader ||
                fullName == EndpointAttributeNames.AspNetFromBody ||
                fullName == EndpointAttributeNames.AspNetFromForm)
                return true;

            // Also check by short name (for cases where type isn't fully resolved)
            if (shortName is "FromRouteAttribute" or "FromRoute" or
                "FromQueryAttribute" or "FromQuery" or
                "FromHeaderAttribute" or "FromHeader" or
                "FromBodyAttribute" or "FromBody" or
                "FromClaimAttribute" or "FromClaim" or
                "FromCookieAttribute" or "FromCookie" or
                "FromFormAttribute" or "FromForm")
                return true;

            // Also check the full display string for partial matches (unresolved types)
            if (fullName?.Contains("FromRoute") == true ||
                fullName?.Contains("FromQuery") == true ||
                fullName?.Contains("FromHeader") == true ||
                fullName?.Contains("FromBody") == true ||
                fullName?.Contains("FromClaim") == true ||
                fullName?.Contains("FromCookie") == true ||
                fullName?.Contains("FromForm") == true)
                return true;

            return false;
        });
    }

    /// <summary>
    ///     The <c>&lt;summary&gt;</c> of any symbol, as one line of prose.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Takes an <see cref="ISymbol" /> because the operation needs the same answer its
    ///         parameters already got. Every operation in the document was published with no
    ///         <c>summary</c> at all — 18 out of 18 in conformance — because the only source was an
    ///         explicit <c>[ApiSummary]</c> that nobody writes, while the <c>&lt;summary&gt;</c>
    ///         above the class was right there and read for parameters alone.
    ///     </para>
    ///     <para>
    ///         Normalised, because a doc comment is written for a human reading source: it arrives
    ///         with newlines, indentation and inline tags, and a generated client puts this string
    ///         into a doc-comment of its own. Inner tags are unwrapped rather than dropped —
    ///         <c>&lt;c&gt;Order&lt;/c&gt;</c> is a word of the sentence, not decoration.
    ///     </para>
    /// </remarks>
    private static string? GetXmlDocSummary(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml))
            return null;

        var summaryStart = xml!.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
        var summaryEnd = xml.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
        if (summaryStart < 0 || summaryEnd <= summaryStart)
            return null;

        var content = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
        var text = new System.Text.StringBuilder(content.Length);
        var inTag = false;

        foreach (var ch in content)
        {
            if (ch == '<') { inTag = true; continue; }
            if (ch == '>') { inTag = false; continue; }
            if (inTag) continue;

            // Newlines and the indentation that follows them collapse to one space; the builder is
            // trimmed at the end, so a leading run costs nothing.
            text.Append(ch is '\r' or '\n' or '\t' ? ' ' : ch);
        }

        var collapsed = System.Text.RegularExpressions.Regex
            .Replace(text.ToString(), @"\s{2,}", " ")
            .Trim();

        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static bool IsServiceType(ITypeSymbol type) => Core.ServiceTypeDetector.IsServiceType(type);

    /// <summary>
    ///     Checks if the type implements ISyncValidator (directly or through inheritance).
    /// </summary>
    private static bool ImplementsISyncValidator(INamedTypeSymbol symbol)
    {
        return symbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == "Pragmatic.Validation.ISyncValidator");
    }

    /// <summary>
    ///     Checks if the type has a specific attribute by fully qualified name.
    /// </summary>
    private static bool HasAttribute(INamedTypeSymbol symbol, string attributeFullName)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == attributeFullName);
    }
}
