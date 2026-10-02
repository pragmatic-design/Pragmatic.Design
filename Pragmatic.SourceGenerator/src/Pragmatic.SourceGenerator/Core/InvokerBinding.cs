using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Whether a property of an operation is filled by the generated invoker — from the caller
///     (<c>[FromCurrentUser]</c>), from the clock (<c>[FromClock]</c>) or from a declared query
///     (<c>[LoadFrom&lt;TQuery&gt;]</c>) — and so is no input of anyone's.
/// </summary>
/// <remarks>
///     One question for every place that decides what a caller may send: the endpoint, which must not
///     read the property from the request, and the query transform. A binding one of them knew and the
///     other did not would be a value the invoker writes and the URL overwrites.
/// </remarks>
internal static class InvokerBinding
{
    private const string LoadFrom = "Pragmatic.Actions.Attributes.LoadFromAttribute";

    /// <summary>Whether the invoker writes the property.</summary>
    public static bool IsBound(IPropertySymbol property)
        => FromCurrentUserReader.IsBound(property) || IsFromTheClock(property) || IsLoadedFromAQuery(property);

    /// <summary>Whether the property carries <c>[FromClock]</c>.</summary>
    public static bool IsFromTheClock(IPropertySymbol property)
        => property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeNames.FromClock);

    /// <summary>Whether the property carries <c>[LoadFrom&lt;TQuery&gt;]</c>.</summary>
    public static bool IsLoadedFromAQuery(IPropertySymbol property) => property.GetAttributes().Any(IsLoadFrom);

    /// <summary>Whether the attribute is a <c>[LoadFrom&lt;TQuery&gt;]</c>.</summary>
    public static bool IsLoadFrom(AttributeData attribute)
        => attribute.AttributeClass is { TypeArguments.Length: 1 } attributeClass
           && attributeClass.OriginalDefinition.ToDisplayString().StartsWith(LoadFrom, StringComparison.Ordinal);
}
