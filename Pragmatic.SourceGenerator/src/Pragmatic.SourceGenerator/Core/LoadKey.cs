using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Whether a property of an operation is an input of a row it preloads — the key a <c>[LoadEntity]</c>,
///     <c>[LoadEntities]</c> or <c>[RequireExists]</c> on its type names, or a parameter of the rule one reads by.
/// </summary>
/// <remarks>
///     Such an input is written to the entity when the entity has a member of its name — a team's
///     <c>ManagerId</c> — and is otherwise no input to map: the body reads the row it finds, not the value
///     that found it. Asked by the mutation's auto-map and by the mapping of a <c>[MapTo]</c>, so the two do
///     not disagree.
/// </remarks>
internal static class LoadKey
{
    private const string LoadEntity = "Pragmatic.Actions.Attributes.LoadEntityAttribute";
    private const string LoadEntities = "Pragmatic.Actions.Attributes.LoadEntitiesAttribute";
    private const string RequireExists = "Pragmatic.Actions.Attributes.RequireExistsAttribute";

    /// <summary>
    ///     Whether the attribute is a <c>[LoadEntity]</c>, a <c>[LoadEntities]</c>, or a <c>[RequireExists]</c> — which
    ///     reads the row too, as an <c>EXISTS</c>, and is counted with them wherever what an operation reaches is.
    /// </summary>
    public static bool IsALoad(AttributeData attribute)
        => attribute.AttributeClass?.OriginalDefinition.ToDisplayString() is { } name
           && (name.StartsWith(LoadEntity, StringComparison.Ordinal)
               || name.StartsWith(LoadEntities, StringComparison.Ordinal)
               || name.StartsWith(RequireExists, StringComparison.Ordinal));

    /// <summary>Whether a load on the property's type names it as its key, or reads by a rule it feeds.</summary>
    public static bool IsNamedByALoad(IPropertySymbol property, Compilation compilation)
        => property.ContainingType.GetAttributes().Any(a =>
               IsALoad(a)
               && a.ConstructorArguments.Length > 0
               && a.ConstructorArguments[0].Value is string key
               && key == property.Name)
           || LoadSpecification.FeedsARule(property, compilation);
}
