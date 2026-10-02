using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.FastEnum.Models;

namespace Pragmatic.SourceGenerator.Features.FastEnum.Transforms;

/// <summary>
///     Transforms [FastEnum] enum declarations into FastEnumModel.
/// </summary>
internal static class FastEnumTransform
{
    public static FastEnumModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol enumSymbol)
            return null;

        if (enumSymbol.TypeKind != TypeKind.Enum)
            return null;

        var underlyingType = enumSymbol.EnumUnderlyingType?.ToDisplayString() ?? "int";
        var ns = enumSymbol.ContainingNamespace.ToDisplayString();

        var isFlags = enumSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");

        // Detect i18n reference
        var hasI18n = context.SemanticModel.Compilation
            .GetTypeByMetadataName("Pragmatic.Internationalization.Providers.ILocalizationProvider") is not null;

        string? i18nPrefix = null;
        if (hasI18n)
            i18nPrefix = I18nNamingHelper.DeriveI18nPrefix(ns, enumSymbol.Name);

        var members = ImmutableArray.CreateBuilder<FastEnumMemberModel>();

        foreach (var member in enumSymbol.GetMembers())
        {
            if (member is not IFieldSymbol field || !field.HasConstantValue)
                continue;

            // Check for [Description] or [Display(Name = ...)] attributes
            string? displayName = null;
            foreach (var attr in field.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (attrName == "System.ComponentModel.DescriptionAttribute" &&
                    attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is string desc)
                {
                    displayName = desc;
                }
                else if (attrName == "System.ComponentModel.DataAnnotations.DisplayAttribute")
                {
                    foreach (var named in attr.NamedArguments)
                    {
                        if (named.Key == "Name" && named.Value.Value is string name)
                            displayName = name;
                    }
                }
            }

            string? i18nKey = null;
            if (hasI18n && i18nPrefix is not null)
                i18nKey = I18nNamingHelper.DeriveI18nKey(i18nPrefix, field.Name);

            members.Add(new FastEnumMemberModel
            {
                Name = field.Name,
                DisplayName = displayName,
                Value = field.ConstantValue?.ToString() ?? "0",
                I18nKey = i18nKey
            });
        }

        return new FastEnumModel
        {
            TypeName = enumSymbol.Name,
            FullTypeName = enumSymbol.ToDisplayString(),
            Namespace = ns,
            Accessibility = enumSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            UnderlyingType = underlyingType,
            IsFlags = isFlags,
            Members = members.ToImmutable(),
            HasI18n = hasI18n,
            I18nKeyPrefix = i18nPrefix,
            IsValid = members.Count > 0
        };
    }
}
