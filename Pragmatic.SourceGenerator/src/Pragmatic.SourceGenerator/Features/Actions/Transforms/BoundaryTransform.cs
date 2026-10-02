using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static class BoundaryTransform
{
    private const string BoundarySuffix = "Boundary";

    public static BoundaryModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetNode is not ClassDeclarationSyntax classDecl)
            return null;

        var symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant();
        var location = LocationInfo.From(classDecl.Identifier.GetLocation());
        var fullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return new BoundaryModel
            {
                Namespace = ns,
                TypeName = symbol.Name,
                FullTypeName = fullTypeName,
                InterfaceName = "",
                InternalInterfaceName = "",
                ImplementationName = "",
                Accessibility = accessibility,
                InvalidReason = BoundaryInvalidReason.NotPartial,
                LocationInfo = location
            };

        if (string.IsNullOrEmpty(ns))
            return new BoundaryModel
            {
                Namespace = ns,
                TypeName = symbol.Name,
                FullTypeName = fullTypeName,
                InterfaceName = "",
                InternalInterfaceName = "",
                ImplementationName = "",
                Accessibility = accessibility,
                InvalidReason = BoundaryInvalidReason.NoNamespace,
                LocationInfo = location
            };

        var (customName, isInternal) = ParseBoundaryAttributes(context.Attributes);
        var readAccessTypes = CollectReadAccessTypes(symbol);

        var interfaceName = DeriveInterfaceName(symbol.Name, customName);
        var internalInterfaceName = DeriveInternalInterfaceName(interfaceName);
        var shortName = StripBoundarySuffix(symbol.Name);
        var implementationName = $"{shortName}LocalActions";

        return new BoundaryModel
        {
            Namespace = ns,
            TypeName = symbol.Name,
            FullTypeName = fullTypeName,
            InterfaceName = interfaceName,
            InternalInterfaceName = internalInterfaceName,
            ImplementationName = implementationName,
            Accessibility = accessibility,
            InvalidReason = BoundaryInvalidReason.None,
            LocationInfo = location,
            ReadAccessTypes = readAccessTypes,
            IsInternal = isInternal,
            DeclaresTransactional = CommitScopeDetector.IsTransactional(symbol)
        };
    }

    private static ImmutableArray<string> CollectReadAccessTypes(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;
            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith("Pragmatic.Actions.Attributes.ReadAccessAttribute"))
                continue;
            if (attrClass.TypeArguments.Length > 0)
                builder.Add(attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
        return builder.ToImmutable();
    }

    private static string DeriveInterfaceName(string typeName, string? customName)
    {
        if (!string.IsNullOrEmpty(customName))
            return customName!;

        var baseName = typeName.EndsWith(BoundarySuffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - BoundarySuffix.Length)
            : typeName;

        return $"I{baseName}Actions";
    }

    private static string DeriveInternalInterfaceName(string interfaceName)
    {
        var lastWordStart = -1;
        for (var i = interfaceName.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(interfaceName[i]))
            {
                lastWordStart = i;
                break;
            }
        }

        if (lastWordStart <= 0)
            return interfaceName + "Internal";

        return interfaceName.Substring(0, lastWordStart) + "Internal" + interfaceName.Substring(lastWordStart);
    }

    private static string StripBoundarySuffix(string typeName)
    {
        return typeName.EndsWith(BoundarySuffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - BoundarySuffix.Length)
            : typeName;
    }

    private static (string? customName, bool isInternal) ParseBoundaryAttributes(ImmutableArray<AttributeData> attributes)
    {
        var attr = attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "Pragmatic.Actions.Attributes.BoundaryAttribute");

        if (attr is null)
            return (null, false);

        string? customName = null;
        var isInternal = false;

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "Name", Value.Value: string name })
                customName = name;
            else if (namedArg is { Key: "Visibility", Value.Value: int visibility })
                isInternal = visibility == 1; // BoundaryVisibility.Internal = 1
        }

        return (customName, isInternal);
    }
}
