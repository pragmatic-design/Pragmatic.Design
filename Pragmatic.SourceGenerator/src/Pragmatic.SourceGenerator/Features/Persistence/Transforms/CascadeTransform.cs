using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms a property with [CascadeOn&lt;TSource&gt;("SourceProperty")] into a <see cref="CascadeModel"/>.
///     Used with ForAttributeWithMetadataName on property declarations.
/// </summary>
internal static class CascadeTransform
{
    public const string CascadeOnAttributeName = "Pragmatic.Persistence.Entity.CascadeOnAttribute`1";

    public static CascadeModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetNode is not PropertyDeclarationSyntax)
            return null;

        if (context.TargetSymbol is not IPropertySymbol targetProperty)
            return null;

        // ForAttributeWithMetadataName already matched CascadeOnAttribute`1
        var attr = context.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        // Extract TSource from generic type argument
        var attrClass = attr.AttributeClass;
        if (attrClass is not { TypeArguments.Length: 1 })
            return null;

        var sourceType = attrClass.TypeArguments[0] as INamedTypeSymbol;
        if (sourceType is null)
            return null;

        // Extract source property name from constructor argument
        if (attr.ConstructorArguments.Length == 0 || attr.ConstructorArguments[0].Value is not string sourceProperty)
            return null;

        // Extract optional Condition from named arguments
        string? condition = null;
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "Condition", Value.Value: string condValue })
                condition = condValue;
        }

        // Target entity is the containing type of the property
        var targetEntity = targetProperty.ContainingType;
        if (targetEntity is null)
            return null;

        var targetNs = targetEntity.ContainingNamespace.IsGlobalNamespace
            ? ""
            : targetEntity.ContainingNamespace.ToDisplayString();

        // Resolve the target entity's boundary so the generated handler can inject the correct keyed
        // DbContext in host mode (host registers DbContext only as AddKeyedScoped by boundary).
        var targetBoundary = EntityTransform.GetBoundaryInfo(targetEntity).FullTypeName;

        // The key is the one the target's declared relation to the source generates. A member found
        // by name would be a hand-written one — the generated key lives in a file this transform
        // cannot see. The answer travels with the model instead of being swallowed by a fallback: a
        // handler that filters on a property the entity does not have is a CS1061 in generated code,
        // and the feature reports PRAG0702 rather than emitting it.
        var declaredKey = Core.TraitPropertyResolver.DeclaredForeignKeyTo(targetEntity, sourceType);
        var fkProperty = declaredKey?.Name ?? $"{sourceType.Name}Id";
        var fkTypeName = declaredKey is { } key
            ? (key.TypeFullName.StartsWith("global::") ? key.TypeFullName : $"global::{key.TypeFullName}")
            : "object";

        return new CascadeModel
        {
            Namespace = targetNs,
            SourceTypeName = sourceType.Name,
            // Without global:: for cascade source map matching with EntityMetadataModel.FullTypeName
            SourceFullTypeName = sourceType.ToDisplayString(),
            // With global:: for code generation in CascadeHandlerTemplate
            SourceQualifiedTypeName = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SourceProperty = sourceProperty,
            TargetTypeName = targetEntity.Name,
            TargetFullTypeName = targetEntity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            TargetProperty = targetProperty.Name,
            TargetPropertyTypeName = targetProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ForeignKeyProperty = fkProperty,
            ForeignKeyTypeName = fkTypeName,
            TargetBoundaryTypeFullName = targetBoundary,
            Condition = condition,
            HasForeignKey = declaredKey is not null,
            Location = LocationInfo.From(targetProperty.Locations.FirstOrDefault())
        };
    }
}
