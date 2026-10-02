using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Transforms;

/// <summary>
///     Reads the privacy classification attributes off Roslyn symbols.
/// </summary>
/// <remarks>
///     Enum arguments arrive as their underlying <c>int</c>, so they are mapped back to member names
///     here. The generator targets netstandard2.0 and must not reference the runtime package whose
///     types it is describing — carrying the member name as a string is what keeps that boundary.
/// </remarks>
internal static class PrivacyAttributeReader
{
    /// <summary><c>DataCategory</c> members, by declaration order.</summary>
    private static readonly string[] Categories =
        ["Identity", "Contact", "Financial", "Location", "Behavioural", "Special"];

    /// <summary><c>ErasureStrategy</c> members, by declaration order.</summary>
    private static readonly string[] Strategies =
        ["Null", "Delete", "Anonymize", "Pseudonymize", "Retain", "DestroyKey"];

    /// <summary>
    ///     Reads the reason from <c>[NotPersonalData]</c>, or null when the property carries none.
    /// </summary>
    /// <remarks>
    ///     An explicit "no" counts as a decision, which is all <c>PRAG2903</c> asks for. The reason is
    ///     mandatory on the attribute and kept here because the processing register lists it beside the
    ///     classified fields: "someone decided" is only useful with what they decided and why.
    /// </remarks>
    public static string? ReadNotPersonalReason(ISymbol property)
    {
        var attr = FindAttribute(property, AttributeNames.PrivacyNotPersonalData);
        if (attr is null)
            return null;

        return attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string reason
            ? reason
            : "";
    }

    /// <summary>Reads <c>[PersonalData]</c> off a property, or null when it carries none.</summary>
    public static PersonalDataModel? ReadPersonalData(ISymbol property)
    {
        var attr = FindAttribute(property, AttributeNames.PrivacyPersonalData);
        if (attr is null)
            return null;

        var category = attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int c
            ? Member(Categories, c)
            : Categories[0];

        var erasure = Strategies[0];
        string? reason = null;
        var encrypted = false;

        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "Erasure" when named.Value.Value is int e:
                    erasure = Member(Strategies, e);
                    break;
                case "Reason":
                    reason = named.Value.Value as string;
                    break;
                case "Encrypted" when named.Value.Value is bool b:
                    encrypted = b;
                    break;
            }
        }

        return new PersonalDataModel
        {
            Category = category,
            Erasure = erasure,
            Reason = reason,
            Encrypted = encrypted
        };
    }

    /// <summary>Reads the identifier property named by <c>[DataSubject]</c>, or null when absent.</summary>
    public static string? ReadSubjectIdentifier(ISymbol type)
        => FirstStringArgument(FindAttribute(type, AttributeNames.PrivacyDataSubject));

    /// <summary>Reads the path property named by <c>[LinksToSubject]</c>, or null when absent.</summary>
    public static string? ReadSubjectPath(ISymbol type)
        => FirstStringArgument(FindAttribute(type, AttributeNames.PrivacyLinksToSubject));

    private static AttributeData? FindAttribute(ISymbol symbol, string fullyQualifiedName)
        => symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == fullyQualifiedName);

    private static string? FirstStringArgument(AttributeData? attr)
        => attr is { ConstructorArguments.Length: > 0 }
            ? attr.ConstructorArguments[0].Value as string
            : null;

    /// <summary>
    ///     Maps an enum's underlying value back to its member name, falling back to the first member for
    ///     a value this generator does not know — a consumer compiled against a newer runtime should get
    ///     a conservative default, not an index out of range during a build.
    /// </summary>
    private static string Member(string[] members, int value)
        => value >= 0 && value < members.Length ? members[value] : members[0];
}
