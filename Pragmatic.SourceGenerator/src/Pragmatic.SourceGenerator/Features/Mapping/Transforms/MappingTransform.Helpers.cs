using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Transforms;

/// <summary>
///     Property model creation helpers for MappingTransform.
/// </summary>
internal static partial class MappingTransform
{
    /// <summary>The position of a type declaration's name, for the diagnostics reported on the type.</summary>
    internal static LocationInfo? TypeLocation(SyntaxNode node)
        => LocationInfo.From((node as BaseTypeDeclarationSyntax)?.Identifier.GetLocation() ?? node.GetLocation());

    /// <summary>The position of a property declaration, for the diagnostics reported on the property.</summary>
    private static LocationInfo? PropertyLocation(IPropertySymbol prop)
        => LocationInfo.From(prop.Locations.FirstOrDefault());

    /// <summary>
    ///     What the projection writes for a source member that is <c>[Projectable]</c>: its body, which
    ///     the database computes, instead of its getter, which EF Core could only run in memory over
    ///     navigations nobody loaded. Null for any other member — the source expression is
    ///     then the projection's too — and for one whose body is not in this compilation.
    /// </summary>
    private static string? ProjectedSource(
        ImmutableArray<IPropertySymbol> sourceProperties, string? sourceExpr, Compilation compilation)
    {
        var member = sourceProperties.FirstOrDefault(p => sourceExpr == $"entity.{p.Name}");
        return member is null ? null : ProjectableBody.Of(member, compilation, "entity");
    }

    /// <summary>
    ///     The projectable member at the end of a path through a navigation — a flattened DTO property
    ///     or an explicit <c>[MapProperty("Customer.OpenBalance")]</c> — read plain, and its body over
    ///     the same navigation. Null when the path has one segment, or ends on anything else.
    /// </summary>
    /// <remarks>
    ///     The direct case is <see cref="ProjectedSource" />'s. Here the owner is not <c>entity</c> but
    ///     the path up to the member, and the null checks the projection writes around the read belong
    ///     to the template, which is why the read is replaced there instead of rewritten here.
    /// </remarks>
    private static (string? Read, string? Body) ProjectedTerminal(
        INamedTypeSymbol sourceSymbol, string? sourceExpr, Compilation compilation)
    {
        if (TerminalMember(sourceSymbol, sourceExpr) is not { Segments.Length: >= 2 } terminal)
            return (null, null);

        var body = ProjectableBody.Of(terminal.Member, compilation, terminal.OwnerPath);
        return body is null ? (null, null) : ("entity." + string.Join(".", terminal.Segments), body);
    }

    /// <summary>
    ///     The navigations a projectable member at the end of <paramref name="sourceExpr" /> reads,
    ///     with the path that reaches them — what the in-memory mapping has to load, since it reads the
    ///     getter and the getter walks them.
    /// </summary>
    private static ImmutableArray<string> ProjectableNavigations(
        INamedTypeSymbol sourceSymbol, string? sourceExpr, Compilation compilation)
    {
        if (TerminalMember(sourceSymbol, sourceExpr) is not { } terminal)
            return ImmutableArray<string>.Empty;

        var prefix = string.Join(".", terminal.Segments.Take(terminal.Segments.Length - 1));
        return ProjectableBody.NavigationsOf(terminal.Member, compilation)
            .Select(navigation => prefix.Length == 0 ? navigation : $"{prefix}.{navigation}")
            .ToImmutableArray();
    }

    /// <summary>
    ///     Whether the source path reads a value that lives in the entity's own row — a
    ///     <c>[ValueObject]</c> or a <c>Money</c>, which the persistence generator maps as an EF Core
    ///     complex type.
    /// </summary>
    /// <remarks>
    ///     It is not a navigation, and the difference is not cosmetic: it is materialised with
    ///     the entity, so it needs no "not loaded" guard — on a <c>struct</c> such as <c>Money</c> that
    ///     guard does not even compile — and it is not something a query can <c>Include</c>.
    ///     <para>
    ///         Two segments is the shape that matters, <c>entity.Address.Street</c>. A deeper path goes
    ///         through a navigation first and keeps every navigation rule.
    ///     </para>
    /// </remarks>
    private static bool ReadsAValueInTheEntitysOwnRow(string? sourceExpr, INamedTypeSymbol sourceSymbol)
    {
        if (sourceExpr is null || !sourceExpr.StartsWith("entity.", StringComparison.Ordinal))
            return false;

        var segments = sourceExpr.Substring("entity.".Length).Replace("?.", ".").Split('.');
        if (segments.Length != 2)
            return false;

        var owner = PropertyAnalyzer.GetAllProperties(sourceSymbol)
            .FirstOrDefault(p => p.Name == segments[0]);

        return owner is not null && Analysis.SqlTranslatableAnalyzer.IsProjectedWhole(owner.Type);
    }

    /// <summary>
    ///     The member a plain <c>entity.A?.B.C</c> source expression ends on, the path to its owner, and
    ///     the segments; null when the expression is anything else — a concatenation, a conversion.
    /// </summary>
    private static (IPropertySymbol Member, string OwnerPath, string[] Segments)? TerminalMember(
        INamedTypeSymbol sourceSymbol, string? sourceExpr)
    {
        if (sourceExpr is null || !sourceExpr.StartsWith("entity.", StringComparison.Ordinal))
            return null;

        var segments = sourceExpr.Substring("entity.".Length).Replace("?.", ".").Split('.');
        if (segments.Any(s => s.Length == 0 || !SyntaxFacts.IsValidIdentifier(s)))
            return null;

        ITypeSymbol current = sourceSymbol;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current is not INamedTypeSymbol named)
                return null;

            var declared = PropertyAnalyzer.GetAllProperties(named)
                .FirstOrDefault(p => p.Name == segments[i]);
            if (declared is not null)
            {
                current = declared.Type;
                continue;
            }

            // A navigation a [Relation] generates is not a symbol during this pass.
            var generated = TraitPropertyResolver.GetGeneratedProperties(named)
                .FirstOrDefault(p => p.Name == segments[i] && p.TypeSymbol is not null && !p.IsCollection);
            if (generated.TypeSymbol is null)
                return null;

            current = generated.TypeSymbol;
        }

        if (current is not INamedTypeSymbol owner)
            return null;

        var terminal = segments[segments.Length - 1];
        var member = PropertyAnalyzer.GetAllProperties(owner).FirstOrDefault(p => p.Name == terminal);
        if (member is null)
            return null;

        var ownerPath = segments.Length == 1
            ? "entity"
            : "entity." + string.Join(".", segments.Take(segments.Length - 1));
        return (member, ownerPath, segments);
    }

    private static PropertyMappingModel CreateIgnoredProperty(IPropertySymbol prop)
    {
        return new PropertyMappingModel
        {
            Location = PropertyLocation(prop),
            PropertyName = prop.Name,
            PropertyType = prop.Type.ToDisplayString(),
            IsNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated,
            IsIgnored = true,
            Resolution = MappingResolution.None
        };
    }

    private static PropertyMappingModel CreateExplicitMapping(
        IPropertySymbol targetProp,
        INamedTypeSymbol sourceSymbol,
        MapPropertyInfo attrInfo,
        string? converterType = null)
    {
        string? sourceExpr = null;
        string? projectionExpr = null;
        string? sourceType = null;
        var sourceNullable = false;
        var resolution = MappingResolution.Explicit;
        string? invalidSourcePath = null;

        if (attrInfo.SourcePaths.Length == 1)
        {
            // Single path - could be nested (flattening)
            var (expr, type, nullable) =
                PropertyPathResolver.ResolvePropertyPath(attrInfo.SourcePaths[0], sourceSymbol);
            sourceExpr = expr;
            sourceType = type;
            sourceNullable = nullable;

            // PRAG0302: the explicit path doesn't resolve — skip the mapping (Resolution=None) so the
            // generated code stays compilable and the error is a clear PRAG, not a CS in generated code.
            if (expr is null)
            {
                invalidSourcePath = attrInfo.SourcePaths[0];
                resolution = MappingResolution.None;
            }
        }
        else if (attrInfo.SourcePaths.Length > 1)
        {
            // Multiple paths - concatenation
            var parts = new List<string>();
            foreach (var path in attrInfo.SourcePaths)
            {
                var (expr, _, _) = PropertyPathResolver.ResolvePropertyPath(path, sourceSymbol);
                if (expr is not null)
                    parts.Add(expr);
                else
                    invalidSourcePath ??= path; // PRAG0302 on the first unresolvable path
            }

            if (invalidSourcePath is not null)
            {
                resolution = MappingResolution.None;
                sourceExpr = null;
            }
            else
            {
                sourceExpr = string.Join($" + \"{attrInfo.Separator}\" + ", parts);
                sourceType = "string";

                // The same join as a projection has to say it: an enum concatenated in SQL is the number
                // the column holds, so each enum part becomes a chain of comparisons. Composed here,
                // beside the form it differs from, rather than recomputed by whoever renders it.
                projectionExpr = ProjectionJoin(attrInfo, parts, sourceSymbol);
            }
        }
        else if (attrInfo.SourcePaths.Length == 0 && !string.IsNullOrEmpty(attrInfo.DefaultValue))
        {
            // No explicit source path, but has default value
            // Try to resolve by property name match
            var (expr, type, nullable) = PropertyPathResolver.ResolvePropertyPath(targetProp.Name, sourceSymbol);
            sourceExpr = expr;
            sourceType = type;
            sourceNullable = nullable;
        }
        else if (attrInfo.SourcePaths.Length == 0 && !string.IsNullOrEmpty(attrInfo.Target))
        {
            // [MapTo] scenario: no SourcePath, but has Target
            // The source is the DTO property itself (this.PropertyName)
            // The target is the nested entity path (Target)
            // For MapFrom analysis, we can try to resolve by property name
            var (expr, type, nullable) = PropertyPathResolver.ResolvePropertyPath(targetProp.Name, sourceSymbol);
            sourceExpr = expr ?? $"entity.{targetProp.Name}";
            sourceType = type ?? targetProp.Type.ToDisplayString();
            sourceNullable = nullable || targetProp.Type.NullableAnnotation == NullableAnnotation.Annotated;
        }

        // Asked once the path is known not to resolve: does its first segment name a relation that was
        // declared and produced no navigation because it crosses a boundary? The property is absent
        // either way — this decides whether the author is sent looking for a typo, or told that the
        // join they want is not one the database can perform.
        var crossBoundary = invalidSourcePath is null
            ? null
            : TraitPropertyResolver.FindCrossBoundaryRelation(
                sourceSymbol, invalidSourcePath.Split('.')[0]);

        var collectionInfo = CollectionAnalyzer.AnalyzeCollectionType(targetProp.Type);
        var nestedInfo = NestedDtoAnalyzer.AnalyzeNestedDto(targetProp.Type, collectionInfo);

        // Detect type conversion for explicit mappings (skip if converter specified)
        var sourceIsEnum = sourceType?.Contains('.') == false && !TypeAnalyzer.IsBuiltInType(sourceType);
        var targetIsEnum = TypeConversionHelper.IsEnumType(targetProp.Type);
        var conversion = converterType is null
            ? TypeConversionHelper.DetectConversion(sourceType, targetProp.Type.ToDisplayString(), sourceIsEnum,
                targetIsEnum)
            : ConversionKind.None;

        return new PropertyMappingModel
        {
            Location = PropertyLocation(targetProp),
            PropertyName = targetProp.Name,
            PropertyType = targetProp.Type.ToDisplayString(),
            IsNullable = targetProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
            IsRequired = targetProp.IsRequired,
            IsInitOnly = PropertyAnalyzer.IsInitOnly(targetProp),
            SourcePaths = attrInfo.SourcePaths,
            Format = attrInfo.Format,
            Separator = attrInfo.Separator,
            ProjectionSourceExpression = projectionExpr,
            DefaultValue = attrInfo.DefaultValue,
            TargetPath = attrInfo.Target,
            ExplicitSourcePathInvalid = invalidSourcePath,
            CrossBoundaryOtherEntity = crossBoundary?.OtherEntity,
            CrossBoundaryOwn = crossBoundary?.OwnBoundary,
            CrossBoundaryOther = crossBoundary?.OtherBoundary,
            Resolution = resolution,
            SourceExpression = sourceExpr,
            SourcePropertyType = sourceType,
            SourceIsNullable = sourceNullable,
            SourceIsEnum = sourceIsEnum,
            TargetIsEnum = targetIsEnum,
            Conversion = conversion,
            ConverterType = converterType,
            CollectionKind = collectionInfo.Kind,
            ElementType = collectionInfo.ElementType,
            IsElementSimple = collectionInfo.IsElementSimple,
            IsNestedDto = nestedInfo.IsNested,
            NestedDtoType = nestedInfo.DtoType,
            IsElementDto = nestedInfo.IsElementDto,
            ElementDtoType = nestedInfo.ElementDtoType,
            IsDictionary = collectionInfo.IsDictionary,
            DictionaryKeyType = collectionInfo.KeyType,
            DictionaryValueType = collectionInfo.ValueType,
            IsDictionaryValueSimple = collectionInfo.IsValueSimple,
            DictionaryValueDtoType = collectionInfo.ValueDtoType,
            IsSqlTranslatable = string.IsNullOrEmpty(attrInfo.Format) && converterType is null
        };
    }

    /// <summary>
    ///     The joined expression a projection needs, or <c>null</c> when it is the same as the one the
    ///     in-memory mapping uses.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Only single-segment paths are examined. A dotted path reaches through a navigation, and
    ///     resolving the type at the end of one is a different job; such a part keeps the concatenation
    ///     it always had.
    /// </remarks>
    private static string? ProjectionJoin(
        MapPropertyInfo attrInfo,
        List<string> parts,
        INamedTypeSymbol sourceSymbol)
    {
        var paths = attrInfo.SourcePaths;
        if (paths.Length != parts.Count)
            return null;

        var rendered = new List<string>(parts.Count);
        var anyEnum = false;

        for (var i = 0; i < parts.Count; i++)
        {
            var members = EnumMembersOf(paths[i], sourceSymbol, out var typeName);
            if (members.Count == 0)
            {
                rendered.Add(parts[i]);
                continue;
            }

            anyEnum = true;
            var branches = string.Concat(members.Select(m => $"{parts[i]} == {typeName}.{m} ? \"{m}\" : "));
            rendered.Add($"({branches}\"\")");
        }

        return anyEnum
            ? string.Join($" + \"{attrInfo.Separator}\" + ", rendered)
            : null;
    }

    /// <summary>The members of the enum a path names, empty when it names anything else.</summary>
    /// <remarks>
    ///     Dotted paths included: a part that reaches through a navigation is an enum just as much as one
    ///     that names a column of this entity, and it was concatenated as its number for exactly as long
    ///     as the other was. The walk belongs to <see cref="PropertyPathResolver" />, which already owns
    ///     it — a second copy here is how two readings of one path come apart.
    /// </remarks>
    private static List<string> EnumMembersOf(string path, INamedTypeSymbol sourceSymbol, out string typeName)
    {
        typeName = "";

        if (PropertyPathResolver.ResolvePropertyType(path, sourceSymbol) is not INamedTypeSymbol named)
            return [];

        // A nullable enum is an enum: the chain's last branch answers for the null.
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            named = named.TypeArguments[0] as INamedTypeSymbol ?? named;

        if (named.TypeKind != TypeKind.Enum)
            return [];

        typeName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return named.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToList();
    }
}
