using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Parses <c>[StartsDelegation(nameof(Prop), Purpose = …, Policy = …)]</c> off an action.
/// </summary>
internal static class StartsDelegationParser
{
    private const string AttributeName = "StartsDelegationAttribute";
    private const string AttributeNamespace = "Pragmatic.Actions.Attributes";

    /// <summary>
    ///     Returns the delegation the action declares, or <c>null</c> when it declares none.
    /// </summary>
    /// <param name="symbol">The action type.</param>
    /// <param name="badSubject">
    ///     Set when the named property is missing or is not a string, with the location to report
    ///     <c>PRAG0423</c> at. The model is then <c>null</c>: emitting a scope over a property that
    ///     does not resolve would produce a delegation to nobody, which <c>ActAs</c> throws on at
    ///     runtime, inside a domain action.
    /// </param>
    public static DelegationScopeModel? Parse(INamedTypeSymbol symbol, out (string Property, LocationInfo? Location)? badSubject)
    {
        badSubject = null;

        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null
                || attrClass.Name != AttributeName
                || attrClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
                continue;

            if (attr.ConstructorArguments.Length != 1
                || attr.ConstructorArguments[0].Value is not string subjectProperty
                || subjectProperty.Length == 0)
                return null;

            if (!IsStringProperty(symbol, subjectProperty))
            {
                badSubject = (subjectProperty, LocationInfo.From(AttributeLocation(attr, symbol)));
                return null;
            }

            string? purpose = null;
            var policy = 0;

            foreach (var named in attr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Purpose" when named.Value.Value is string p:
                        purpose = p;
                        break;
                    case "Policy" when named.Value.Value is int value:
                        policy = value;
                        break;
                }
            }

            return new DelegationScopeModel
            {
                SubjectPropertyName = subjectProperty,
                Purpose = purpose,
                Policy = policy
            };
        }

        return null;
    }

    /// <summary>
    ///     Walks the base types too: an action can inherit the property it delegates on.
    /// </summary>
    private static bool IsStringProperty(INamedTypeSymbol symbol, string propertyName)
    {
        for (var type = symbol; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(propertyName))
            {
                if (member is IPropertySymbol property)
                    return property.Type.SpecialType == SpecialType.System_String;
            }
        }

        return false;
    }

    private static Location? AttributeLocation(AttributeData attr, INamedTypeSymbol fallback)
        => attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
           ?? fallback.Locations.FirstOrDefault();
}
