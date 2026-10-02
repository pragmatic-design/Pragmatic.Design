using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Redaction.Transforms;

/// <summary>
///     How a classified member is named in the map, shared by the two passes that build it.
/// </summary>
/// <remarks>
///     One spelling, because the attribute-driven pass and the path pass must agree: a name resolved
///     one way in the first and another in the second would produce a map whose entries look alike
///     and match different payloads.
/// </remarks>
internal static class RedactionNaming
{
    /// <summary>
    ///     The name the payload carries. <c>[JsonPropertyName]</c> wins; otherwise the CLR name,
    ///     which the consumer compares case-insensitively because the payload may be camelCase.
    /// </summary>
    public static string SerializedName(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "JsonPropertyNameAttribute" } cls
                || cls.ContainingNamespace?.ToDisplayString() != "System.Text.Json.Serialization")
                continue;

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string renamed
                && !string.IsNullOrEmpty(renamed))
                return renamed;
        }

        return property.Name;
    }

    /// <summary>
    ///     The enum MEMBER name, not its numeric value. A DataCategory reaches the attribute as a
    ///     boxed underlying value, and "1" says nothing to whoever reads the map.
    /// </summary>
    public static string? CategoryName(TypedConstant constant)
    {
        if (constant.Value is null)
            return null;

        if (constant.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue && Equals(member.ConstantValue, constant.Value))
                    return member.Name;
            }
        }

        return constant.Value.ToString();
    }
}
