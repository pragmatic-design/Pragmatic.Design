using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration;

internal static class ConfigurationTransform
{
    // DataAnnotation attribute FQNs we recognize for validation generation
    private static readonly HashSet<string> KnownValidationAttributes = new()
    {
        "System.ComponentModel.DataAnnotations.RequiredAttribute",
        "System.ComponentModel.DataAnnotations.RangeAttribute",
        "System.ComponentModel.DataAnnotations.MaxLengthAttribute",
        "System.ComponentModel.DataAnnotations.MinLengthAttribute",
        "System.ComponentModel.DataAnnotations.StringLengthAttribute",
        "System.ComponentModel.DataAnnotations.RegularExpressionAttribute",
        "System.ComponentModel.DataAnnotations.EmailAddressAttribute",
        "System.ComponentModel.DataAnnotations.PhoneAttribute",
        "System.ComponentModel.DataAnnotations.UrlAttribute",
    };

    public static ConfigurationModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var attribute = context.Attributes[0];

        var sectionPath = attribute.GetNamedArgument<string>("SectionPath");
        if (string.IsNullOrEmpty(sectionPath))
            sectionPath = InferSectionPath(symbol.Name);

        var validateOnStart = attribute.GetNamedArgument<bool?>("ValidateOnStart") ?? true;

        var properties = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public &&
                        p is { IsStatic: false, SetMethod: not null })
            .Select(TransformProperty)
            .ToImmutableArray();

        var declared = symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Select(m => (Method: m, Attribute: InvariantAttribute(m)))
            .Where(x => x.Attribute is not null)
            .ToList();

        var invariants = declared
            .Where(x => HasInvariantShape(x.Method))
            .Select(x => TransformInvariant(x.Method, x.Attribute!))
            .ToImmutableArray();

        var misshapen = declared
            .Where(x => !HasInvariantShape(x.Method))
            .Select(x => new MisshapenInvariantModel
            {
                MethodName = x.Method.Name,
                Location = LocationInfo.From(x.Method.Locations.FirstOrDefault()),
            })
            .ToImmutableArray();

        return new ConfigurationModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            SectionPath = sectionPath!,
            ValidateOnStart = validateOnStart,
            IsPartial = symbol.IsPartial(),
            IsStaticOrAbstract = symbol.IsStatic || symbol.IsAbstract,
            Properties = properties,
            Invariants = invariants,
            MisshapenInvariants = misshapen
        };
    }

    private static ConfigurationPropertyModel TransformProperty(IPropertySymbol property)
    {
        var validationAttrs = property.GetAttributes()
            .Where(a => a.AttributeClass is not null &&
                        KnownValidationAttributes.Contains(a.AttributeClass.ToDisplayString()))
            .Select(TransformValidationAttribute)
            .ToImmutableArray();

        var isRequired = property.IsRequired ||
                         validationAttrs.Any(a => a.AttributeName == "Required");

        var isSensitive = property.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "SensitiveAttribute" } sensitive &&
            sensitive.ContainingNamespace.ToDisplayString() == "Pragmatic.Configuration");

        return new ConfigurationPropertyModel
        {
            Name = property.Name,
            TypeFullName = property.Type.ToRenderName(),
            IsRequired = isRequired,
            HasValidationAttributes = !validationAttrs.IsEmpty,
            HasDefaultValue = HasInitializer(property),
            IsSensitive = isSensitive,
            ValidationAttributes = validationAttrs
        };
    }

    private static AttributeData? InvariantAttribute(IMethodSymbol method)
        => method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { Name: "ConfigInvariantAttribute" } cls &&
            cls.ContainingNamespace.ToDisplayString() == "Pragmatic.Configuration");

    /// <summary>What the generated validator can call: a parameterless instance method returning <c>bool</c>.</summary>
    private static bool HasInvariantShape(IMethodSymbol method)
        => !method.IsStatic && method.Parameters.Length == 0 && method.ReturnType.SpecialType == SpecialType.System_Boolean;

    /// <summary>Maps a well-shaped <c>[ConfigInvariant("msg")]</c> method to a model.</summary>
    private static InvariantModel TransformInvariant(IMethodSymbol method, AttributeData attribute)
    {
        var message = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value?.ToString() ?? method.Name
            : method.Name;

        return new InvariantModel { MethodName = method.Name, Message = message };
    }

    private static bool HasInitializer(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is PropertyDeclarationSyntax { Initializer: not null })
                return true;
        return false;
    }

    private static ValidationAttributeModel TransformValidationAttribute(AttributeData attr)
    {
        var name = attr.AttributeClass!.Name;
        if (name.EndsWith("Attribute"))
            name = name.Substring(0, name.Length - "Attribute".Length);

        var ctorArgs = attr.ConstructorArguments
            .Select(a => TypedConstantToString(a))
            .ToImmutableArray();

        var namedArgs = attr.NamedArguments
            .Select(a => (a.Key, TypedConstantToString(a.Value)))
            .ToImmutableArray();

        return new ValidationAttributeModel
        {
            AttributeName = name,
            ConstructorArgs = ctorArgs,
            NamedArgs = namedArgs
        };
    }

    private static string TypedConstantToString(TypedConstant constant)
    {
        if (constant.IsNull)
            return "null";
        if (constant.Kind == TypedConstantKind.Primitive)
            return constant.Value?.ToString() ?? "null";
        return constant.Value?.ToString() ?? "null";
    }

    /// <summary>
    /// Infers section path from class name by removing "Options" suffix.
    /// BookingOptions → "Booking", PaymentOptions → "Payment", MyConfig → "MyConfig"
    /// </summary>
    internal static string InferSectionPath(string typeName)
    {
        const string suffix = "Options";
        if (typeName.Length > suffix.Length && typeName.EndsWith(suffix))
            return typeName.Substring(0, typeName.Length - suffix.Length);
        return typeName;
    }
}
