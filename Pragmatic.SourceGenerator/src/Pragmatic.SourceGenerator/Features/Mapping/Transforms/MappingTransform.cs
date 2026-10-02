using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Transforms;

/// <summary>
///     Transform logic for [MapFrom] and [MapTo] attributes.
/// </summary>
/// <remarks>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>MappingTransform.cs - Main Transform methods and type helpers</description>
///             </item>
///             <item>
///                 <description>MappingTransform.PropertyMappings.cs - Property extraction and resolution</description>
///             </item>
///             <item>
///                 <description>MappingTransform.Analysis.cs - Property mapping analysis</description>
///             </item>
///             <item>
///                 <description>MappingTransform.Helpers.cs - Property model creation helpers</description>
///             </item>
///             <item>
///                 <description>MappingTransform.Projection.cs - Nested projection extraction</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal static partial class MappingTransform
{
    // =========================================================================
    // Transform Methods
    // =========================================================================

    /// <summary>
    ///     Transforms [MapFrom] attribute to MappingModel.
    /// </summary>
    public static MappingModel? TransformMapFrom(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        // Get source type from attribute
        var attribute = context.Attributes.FirstOrDefault();

        if (attribute?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return null;

        // An open type argument has no properties to read. Returning null would let the attribute
        // compile with no mapper and a green build, so the model carries the refusal and PRAG0338
        // reports it.
        var sourceType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (sourceType is null)
            return new MappingModel
            {
                TypeName = targetSymbol.Name,
                Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                TypeKind = GetTypeKind(targetSymbol),
                Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : targetSymbol.ContainingNamespace.ToDisplayString(),
                HasMapFrom = true,
                UnusableGenericArgument = true,
                Location = TypeLocation(context.TargetNode)
            };

        // Check if partial — create model with error flag instead of returning null
        if (!IsPartialType(context.TargetNode))
            return new MappingModel
            {
                TypeName = targetSymbol.Name,
                Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                TypeKind = GetTypeKind(targetSymbol),
                Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : targetSymbol.ContainingNamespace.ToDisplayString(),
                HasMapFrom = true,
                MissingPartial = true,
                Location = TypeLocation(context.TargetNode)
            };

        // Check for [GenerateProjection] and its MaxDepth (default 5) BEFORE property extraction —
        // the depth cap flows into nested projection inlining.
        var projectionAttr = targetSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Mapping.Attributes.GenerateProjectionAttribute");
        var hasProjection = projectionAttr is not null;
        var projectionMaxDepth = 5;
        if (projectionAttr is not null)
            foreach (var na in projectionAttr.NamedArguments)
                if (na is { Key: "MaxDepth", Value.Value: int md and > 0 })
                    projectionMaxDepth = md;

        // Extract property mappings
        var properties = ExtractPropertyMappings(
            targetSymbol, sourceType, context.SemanticModel.Compilation, ct, projectionMaxDepth);

        // Check for [GenerateBodyOnlyVariant]
        var hasBodyOnlyVariant = targetSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Mapping.Attributes.GenerateBodyOnlyVariantAttribute");

        // Detect a user-declared CustomizeMapping hook so PRAG0319 can warn that it is
        // ignored by EF Core projection generation.
        var hasCustomizeMapping = targetSymbol.GetMembers("CustomizeMapping")
            .OfType<IMethodSymbol>()
            .Any();

        // Check for circular references
        var hasCircular =
            CircularReferenceDetector.DetectCircularReferences(targetSymbol, sourceType, new HashSet<string>());

        // Infer required navigation paths from resolved properties, then follow the DTOs this one
        // nests. A DTO's own paths are all it can know: OrderDto carrying List<OrderLineDto> works out
        // that it needs OrderLines, and nothing in its own shape says OrderLineDto flattens
        // Product.Name and therefore wants OrderLines.Product as well. The generator walks it here
        // rather than composing the far side's list at runtime — a literal list costs nothing to
        // initialise and, unlike two static initialisers naming each other, a cycle is bounded by the
        // visited set instead of deadlocking.
        var requiredNavigations = ComputeRequiredNavigationsDeep(
            properties,
            context.SemanticModel.Compilation,
            ct,
            projectionMaxDepth,
            new HashSet<string>(StringComparer.Ordinal) { targetSymbol.ToDisplayString() });

        // PRAG0325 (Hidden, reverse coverage): source properties not consumed by any mapping.
        var unmappedSource = ComputeUnmappedSourceProperties(properties, sourceType);

        // [MapDerived<TSource, TDto>]: polymorphic dispatch pairs (declaration order), validated
        // against the contract (derived source : source, derived DTO : this DTO) — PRAG0330 otherwise.
        var (derivedMappings, invalidDerived) = ExtractDerivedMappings(targetSymbol, sourceType);

        // FromEntity must construct via the primary constructor when the DTO has no public parameterless
        // ctor (a positional record). Object-initialiser DTOs keep this empty and are unchanged.
        var hasParameterlessCtor = targetSymbol.InstanceConstructors
            .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        var dtoConstructorParams = hasParameterlessCtor
            ? EquatableArray<ConstructorParameterModel>.Empty
            : ConstructorAnalyzer.ExtractConstructorInfo(targetSymbol, targetSymbol).parameters;

        return new MappingModel
        {
            Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : targetSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = targetSymbol.Name,
            Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = GetTypeKind(targetSymbol),
            IsRecord = targetSymbol.IsRecord,
            IsValueType = targetSymbol.IsValueType,
            InheritsAMappedDto = InheritsAMappedDto(targetSymbol),
            HasMapFrom = true,
            SourceTypeFullName = sourceType.ToDisplayString(),
            SourceTypeName = sourceType.Name,
            SourceTypeIsValueType = sourceType.IsValueType,
            Properties = properties,
            GenerateProjection = hasProjection,
            GenerateBodyOnlyVariant = hasBodyOnlyVariant,
            HasCustomizeMapping = hasCustomizeMapping,
            HasCircularReferences = hasCircular,
            RequiredNavigations = requiredNavigations,
            DtoConstructorParameters = dtoConstructorParams,
            UnmappedSourceProperties = unmappedSource,
            ProjectionMaxDepth = projectionMaxDepth,
            DerivedMappings = derivedMappings,
            InvalidDerivedMappings = invalidDerived,
            Location = TypeLocation(context.TargetNode)
        };
    }

    /// <summary>
    ///     Extracts [MapDerived&lt;TDerivedSource, TDerivedDto&gt;] pairs in declaration order,
    ///     validating that the derived source derives the [MapFrom] source and the derived DTO derives
    ///     this DTO (else the generated dispatch would not compile — PRAG0330).
    /// </summary>
    private static (ImmutableArray<DerivedMappingModel> Valid, ImmutableArray<string> Invalid)
        ExtractDerivedMappings(INamedTypeSymbol dtoSymbol, INamedTypeSymbol sourceType)
    {
        var valid = ImmutableArray.CreateBuilder<DerivedMappingModel>();
        var invalid = ImmutableArray.CreateBuilder<string>();

        foreach (var attr in dtoSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.OriginalDefinition.ToDisplayString()
                    .StartsWith("Pragmatic.Mapping.Attributes.MapDerivedAttribute") != true
                || attr.AttributeClass is not { TypeArguments.Length: 2 } attrType
                || attrType.TypeArguments[0] is not INamedTypeSymbol derivedSource
                || attrType.TypeArguments[1] is not INamedTypeSymbol derivedDto)
                continue;

            var sourceOk = IsSameOrDerivedFrom(derivedSource, sourceType)
                && !SymbolEqualityComparer.Default.Equals(derivedSource, sourceType);
            var dtoOk = IsSameOrDerivedFrom(derivedDto, dtoSymbol)
                && !SymbolEqualityComparer.Default.Equals(derivedDto, dtoSymbol);

            if (sourceOk && dtoOk)
                valid.Add(new DerivedMappingModel(
                    derivedSource.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    derivedDto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            else
                invalid.Add(derivedDto.Name);
        }

        return (valid.ToImmutable(), invalid.ToImmutable());
    }

    /// <summary>
    ///     PRAG0325: source-entity properties whose name never appears as the first segment of any
    ///     mapping's source expression or explicit source path — i.e. data the DTO silently drops.
    /// </summary>
    private static ImmutableArray<string> ComputeUnmappedSourceProperties(
        ImmutableArray<PropertyMappingModel> properties,
        INamedTypeSymbol sourceType)
    {
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
            if (prop.IsIgnored || prop.Resolution == MappingResolution.None)
                continue;

            foreach (var path in prop.SourcePaths)
            {
                var dot = path.IndexOf('.');
                consumed.Add(dot < 0 ? path : path.Substring(0, dot));
            }

            // Extract every "entity.{Identifier}" first segment from the source expression
            // (covers direct match, flattening "entity.Address?.City", and concatenations).
            var expr = prop.SourceExpression;
            if (expr is null)
                continue;
            var idx = 0;
            const string marker = "entity.";
            while ((idx = expr.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
            {
                idx += marker.Length;
                var start = idx;
                while (idx < expr.Length && (char.IsLetterOrDigit(expr[idx]) || expr[idx] == '_'))
                    idx++;
                if (idx > start)
                    consumed.Add(expr.Substring(start, idx - start));
            }
        }

        // Declared members and the ones the generators will add — traits, foreign keys, navigations —
        // the same set PropertyPathResolver resolves a path against. Counting only the declared ones
        // left the coverage blind to exactly the members a DTO is most likely to forget: on an entity
        // written the way this framework recommends, most of them are generated.
        var declared = PropertyAnalyzer.GetAllProperties(sourceType).Select(p => p.Name);
        var generated = Core.TraitPropertyResolver.GetGeneratedProperties(sourceType).Select(vp => vp.Name);

        return declared.Concat(generated)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !consumed.Contains(name))
            .ToImmutableArray();
    }

    /// <summary>
    ///     Transforms [MapTo] attribute to MappingModel.
    /// </summary>
    public static MappingModel? TransformMapTo(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        // Get target type from attribute
        var attribute = context.Attributes.FirstOrDefault();

        if (attribute?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return null;

        // Same refusal as the read side, same reason: without a closed type there is no shape.
        var entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (entityType is null)
            return new MappingModel
            {
                TypeName = targetSymbol.Name,
                Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                TypeKind = GetTypeKind(targetSymbol),
                Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : targetSymbol.ContainingNamespace.ToDisplayString(),
                HasMapTo = true,
                UnusableGenericArgument = true,
                Location = TypeLocation(context.TargetNode)
            };

        // Check if partial — create model with error flag instead of returning null
        if (!IsPartialType(context.TargetNode))
            return new MappingModel
            {
                TypeName = targetSymbol.Name,
                Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                TypeKind = GetTypeKind(targetSymbol),
                Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : targetSymbol.ContainingNamespace.ToDisplayString(),
                MissingPartial = true,
                HasMapTo = true,
                Location = TypeLocation(context.TargetNode)
            };

        // Extract constructor info
        var (constructorParams, hasExplicit) = ConstructorAnalyzer.ExtractConstructorInfo(entityType, targetSymbol);

        // Extract property mappings (reverse direction)
        var properties = ExtractPropertyMappingsForMapTo(targetSymbol, entityType, context.SemanticModel.Compilation, ct);

        // Check if DTO also has [Patch<T>] attribute (from Pragmatic.Persistence)
        var hasMutation = HasMutationAttribute(targetSymbol, entityType);

        var writtenNavigations = ComputeWrittenNavigationsDeep(
            properties,
            context.SemanticModel.Compilation,
            ct,
            new HashSet<string>(StringComparer.Ordinal) { targetSymbol.ToDisplayString() });

        return new MappingModel
        {
            Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : targetSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = targetSymbol.Name,
            Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = GetTypeKind(targetSymbol),
            IsRecord = targetSymbol.IsRecord,
            IsValueType = targetSymbol.IsValueType,
            InheritsAMappedDto = InheritsAMappedDto(targetSymbol),
            HasMapTo = true,
            HasMutationAttribute = hasMutation,
            TargetTypeFullName = entityType.ToDisplayString(),
            TargetTypeName = entityType.Name,
            TargetTypeIsValueType = entityType.IsValueType,
            Properties = properties,
            WrittenNavigations = writtenNavigations,
            ConstructorParameters = constructorParams,
            HasExplicitConstructor = hasExplicit,
            TargetHasParameterlessFactory =
                Core.TraitPropertyResolver.WillHaveParameterlessFactory(entityType),
            Location = TypeLocation(context.TargetNode)
        };
    }

    /// <summary>
    ///     A <c>Mutation&lt;TEntity&gt;</c> read as if it carried <c>[MapTo&lt;TEntity&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Because it <b>is</b> one: a set of properties written onto an entity. Deducing it from
    ///         the base type lets the mutation inherit what Mapping already does — converters, renames,
    ///         dotted targets, type checking — instead of rewriting it in a second mapper.
    ///     </para>
    ///     <para>
    ///         The model carries <c>IsMutationBody</c>, which tells the template to emit <b>only</b>
    ///         <c>ApplyToLoaded</c>: <c>ToEntity</c> has no callers (the invoker loads the entity) and
    ///         <c>WrittenNavigations</c> belongs to Actions, which composes the children's lists at
    ///         runtime. Two generators emitting the same member would give <c>CS0102</c>.
    ///     </para>
    /// </remarks>
    public static MappingModel? TransformMutation(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        // One refusal point and no diagnostic: these are the three conditions Actions checks on the
        // same attribute and reports out loud — «must be partial», «must inherit from
        // Mutation<TEntity>». This is the second reader of [Mutation], not its voice: saying the same
        // thing twice would give two warnings for one defect.
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol
            || !IsPartialType(context.TargetNode)
            || Analysis.MutationAnalyzer.EntityOf(targetSymbol) is not { } entityType)
            return null;

        var properties = ExtractPropertyMappingsForMapTo(targetSymbol, entityType, context.SemanticModel.Compilation, ct);

        return new MappingModel
        {
            Namespace = targetSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : targetSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = targetSymbol.Name,
            Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = GetTypeKind(targetSymbol),
            IsRecord = targetSymbol.IsRecord,
            IsValueType = targetSymbol.IsValueType,
            InheritsAMappedDto = InheritsAMappedDto(targetSymbol),
            HasMapTo = true,
            IsMutationBody = true,
            TargetTypeFullName = entityType.ToDisplayString(),
            TargetTypeName = entityType.Name,
            TargetTypeIsValueType = entityType.IsValueType,
            Properties = properties,
            TargetHasParameterlessFactory =
                Core.TraitPropertyResolver.WillHaveParameterlessFactory(entityType),
            Location = TypeLocation(context.TargetNode)
        };
    }

    // =========================================================================
    // Type Helpers
    // =========================================================================

    private static bool IsPartialType(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.PartialKeyword),
            RecordDeclarationSyntax r => r.Modifiers.Any(SyntaxKind.PartialKeyword),
            StructDeclarationSyntax s => s.Modifiers.Any(SyntaxKind.PartialKeyword),
            _ => false
        };
    }


    /// <summary>
    ///     Whether the type carries <c>[Patch&lt;TEntity&gt;]</c> for this same entity.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The name says Mutation and the check reads Patch. It is the Patch attribute that
    ///     matters here — a <c>[Patch]</c> DTO owns its own update semantics, so this generator does
    ///     not emit <c>ApplyTo</c> for it — and no mutation attribute is involved.
    /// </remarks>
    private static bool HasMutationAttribute(INamedTypeSymbol type, INamedTypeSymbol entityType)
    {
        return type.GetAttributes().Any(a =>
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
                return false;

            // Check if it's PatchAttribute<T>
            if (!attrClass.OriginalDefinition.ToDisplayString()
                    .StartsWith("Pragmatic.Persistence.Patch.PatchAttribute"))
                return false;

            // Check if generic argument matches entity type
            if (attrClass.TypeArguments.Length != 1)
                return false;

            return SymbolEqualityComparer.Default.Equals(attrClass.TypeArguments[0], entityType);
        });
    }

    // =========================================================================
    // Navigation Inference
    // =========================================================================

    /// <summary>
    ///     Computes the required navigation paths from resolved property mappings.
    ///     Navigation paths are inferred from multi-segment source paths, nested DTOs,
    ///     and collection DTOs that require eager loading.
    /// </summary>
    private static ImmutableArray<string> ComputeRequiredNavigations(
        ImmutableArray<PropertyMappingModel> properties)
    {
        var navigations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in properties)
        {
            if (prop.IsIgnored || prop.Resolution == MappingResolution.None)
                continue;

            // What a projectable member's getter walks: the in-memory mapping reads the getter.
            // Each path is a navigation all the way down, so every prefix is one too.
            foreach (var path in prop.ProjectableNavigations)
                AddNavigationSegments(path, lastSegmentIsNavigation: true, navigations);

            var lastSegmentIsNavigation = prop.IsNestedDto ||
                                          (prop.CollectionKind != CollectionKind.None && prop.IsElementDto);

            // Case 1: Explicit source paths from [MapProperty("Customer.Name")]
            if (prop.SourcePaths.Length > 0)
            {
                foreach (var path in prop.SourcePaths)
                    AddNavigationSegments(path, lastSegmentIsNavigation, navigations);
                continue;
            }

            // Case 2: Auto-resolved via SourceExpression (direct match, flattening)
            // Skip concatenation — those are composite expressions, not navigation paths — and skip a
            // value that lives in the entity's own row: a flattened [ValueObject] or Money declared
            // "Address" here, and Include("Address") on a complex type is not a thing EF can do.
            if (prop.Resolution != MappingResolution.Concatenation
                && !prop.SourceIsSameRowValue
                && prop.SourceExpression is not null)
            {
                var cleanPath = CleanEntityPath(prop.SourceExpression);
                if (cleanPath is not null)
                    AddNavigationSegments(cleanPath, lastSegmentIsNavigation, navigations);
            }
        }

        return navigations.OrderBy(n => n).ToImmutableArray();
    }

    /// <summary>
    ///     This DTO's navigations, plus those of the DTOs it nests, prefixed by the path that reaches
    ///     them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Depth <em>within</em> a path was already covered — <c>Customer.Address.City</c> yields
    ///         <c>Customer</c> and <c>Customer.Address</c>. Depth <em>through a DTO</em> was not, and it
    ///         is the common one: a read shape almost always carries element DTOs, and whatever those
    ///         flatten has to be loaded too or it resolves to null on a query and throws on a mutation.
    ///     </para>
    ///     <para>
    ///         <paramref name="visiting" /> holds the DTOs on the current branch, so a DTO that leads
    ///         back to one already on it contributes its own paths and stops. That bounds
    ///         self-referencing and mutually-referencing shapes without excluding them, which is what a
    ///         runtime composition could not have done.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<string> ComputeRequiredNavigationsDeep(
        ImmutableArray<PropertyMappingModel> properties,
        Compilation compilation,
        CancellationToken ct,
        int projectionMaxDepth,
        HashSet<string> visiting)
    {
        var navigations = new HashSet<string>(ComputeRequiredNavigations(properties), StringComparer.Ordinal);

        foreach (var prop in properties)
        {
            ct.ThrowIfCancellationRequested();

            if (prop.IsIgnored || prop.Resolution == MappingResolution.None)
                continue;

            var nestedDtoName = prop.IsNestedDto
                ? prop.NestedDtoType
                : prop is { IsElementDto: true, CollectionKind: not CollectionKind.None }
                    ? prop.ElementType
                    : null;

            if (string.IsNullOrEmpty(nestedDtoName))
                continue;

            // A DTO over the same row reaches its navigations from the root itself.
            var prefix = prop.IsSameRow ? "" : NavigationPathOf(prop);
            if (prefix is null)
                continue;

            var nested = ResolveDtoSymbol(compilation, nestedDtoName!);
            if (nested is null)
                continue;

            var key = nested.ToDisplayString();
            if (!visiting.Add(key))
                continue;

            var nestedSource = MapFromSourceOf(nested);
            if (nestedSource is not null)
            {
                var nestedProperties = ExtractPropertyMappings(nested, nestedSource, compilation, ct, projectionMaxDepth);
                foreach (var deep in ComputeRequiredNavigationsDeep(
                             nestedProperties, compilation, ct, projectionMaxDepth, visiting))
                {
                    navigations.Add(prefix.Length == 0 ? deep : $"{prefix}.{deep}");
                }
            }

            visiting.Remove(key);
        }

        return navigations.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray();
    }

    /// <summary>The full source path a nested or element DTO property reads from, or null.</summary>
    private static string? NavigationPathOf(PropertyMappingModel prop)
    {
        if (prop.SourcePaths.Length > 0)
            return prop.SourcePaths[0];

        return prop.Resolution == MappingResolution.Concatenation || prop.SourceExpression is null
            ? null
            : CleanEntityPath(prop.SourceExpression);
    }

    /// <summary>The DTO type behind a name the property model carries, with or without the global alias.</summary>
    private static INamedTypeSymbol? ResolveDtoSymbol(Compilation compilation, string typeName)
    {
        var name = typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;

        return compilation.GetTypeByMetadataName(name);
    }

    /// <summary>The entity a DTO declares it maps from, or null when it declares none.</summary>
    /// <summary>
    ///     The navigations this DTO writes, plus those written by the DTOs it nests, prefixed by the
    ///     path that reaches them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The write-side twin of <see cref="ComputeRequiredNavigationsDeep" />, and it cannot be
    ///         the same walk: that one follows <c>SourcePaths</c> and recurses through
    ///         <c>[MapFrom]</c>, while a write follows <c>Target</c> and recurses through
    ///         <c>[MapTo]</c>. On a bidirectional DTO whose two shapes differ, the two walks reach
    ///         different sets.
    ///     </para>
    ///     <para>
    ///         A dotted target contributes its intermediate segments: <c>Target = "Customer.Name"</c>
    ///         writes into <c>Customer</c>, and the generated <c>??= new()</c> would build a second
    ///         one if it were not loaded.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<string> ComputeWrittenNavigationsDeep(
        ImmutableArray<PropertyMappingModel> properties,
        Compilation compilation,
        CancellationToken ct,
        HashSet<string> visiting)
    {
        var navigations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in properties)
        {
            ct.ThrowIfCancellationRequested();

            if (prop.IsIgnored || prop.Resolution == MappingResolution.None)
                continue;

            // The identity of the row is never written, so it never has to be loaded for a write.
            if (prop is { IsIdProperty: true, ForceIncludeId: false })
                continue;

            var target = prop.TargetPath ?? prop.PropertyName;
            if (string.IsNullOrEmpty(target))
                continue;

            var writesACollection = prop.CollectionWrite is { } collection
                                    && !string.Equals(collection.Strategy, "Ignore", StringComparison.Ordinal);
            var writesAReference = prop is { IsNestedDto: true, NestedDtoType: not null and not "" };

            // A dotted target reaches through navigations even for a scalar: everything but the last
            // segment is written into and therefore has to be there.
            AddNavigationSegments(target, writesACollection || writesAReference, navigations);

            if (!writesACollection && !writesAReference)
                continue;

            var nestedDtoName = writesAReference ? prop.NestedDtoType : prop.ElementDtoType;
            if (string.IsNullOrEmpty(nestedDtoName))
                continue;

            var nested = ResolveDtoSymbol(compilation, nestedDtoName!);
            if (nested is null)
                continue;

            var key = nested.ToDisplayString();
            if (!visiting.Add(key))
                continue;

            if (MapToTargetOf(nested) is { } nestedTarget)
            {
                var nestedProperties = ExtractPropertyMappingsForMapTo(nested, nestedTarget, compilation, ct);
                foreach (var deep in ComputeWrittenNavigationsDeep(
                             nestedProperties, compilation, ct, visiting))
                {
                    navigations.Add($"{target}.{deep}");
                }
            }

            visiting.Remove(key);
        }

        return navigations.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray();
    }

    /// <summary>The entity a nested DTO reads from, or null when it declares no <c>[MapFrom]</c>.</summary>
    private static INamedTypeSymbol? MapFromSourceOf(INamedTypeSymbol dto)
        => EntityBehind(dto, "MapFromAttribute");

    /// <summary>
    ///     Whether a base of this DTO is itself one this generator writes mapping members onto.
    /// </summary>
    /// <remarks>
    ///     Which is the shape <c>[MapDerived]</c> requires — <c>PRAG0330</c> refuses a derived DTO that
    ///     does not inherit the base — and the answer decides whether the statics carry <c>new</c>.
    ///     Every base is asked, not only the immediate one: a three-level hierarchy hides just the same.
    /// </remarks>
    private static bool InheritsAMappedDto(INamedTypeSymbol dto)
    {
        for (var baseType = dto.BaseType; baseType is not null; baseType = baseType.BaseType)
            if (MapFromSourceOf(baseType) is not null || MapToTargetOf(baseType) is not null)
                return true;

        return false;
    }

    /// <summary>The entity a nested DTO writes to, or null when it declares no <c>[MapTo]</c>.</summary>
    private static INamedTypeSymbol? MapToTargetOf(INamedTypeSymbol dto)
        => EntityBehind(dto, "MapToAttribute");

    /// <summary>The single type argument of a mapping attribute on <paramref name="dto" />.</summary>
    /// <remarks>
    ///     One helper for the two directions: they differ only in the attribute they look for, and two
    ///     copies of the same walk drift the moment one of them learns something.
    /// </remarks>
    private static INamedTypeSymbol? EntityBehind(INamedTypeSymbol dto, string attributeName)
    {
        foreach (var attribute in dto.GetAttributes())
        {
            if (attribute.AttributeClass is not { TypeArguments.Length: 1 } attrClass)
                continue;

            if (string.Equals(attrClass.Name, attributeName, StringComparison.Ordinal))
                return attrClass.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    /// <summary>
    ///     Adds navigation segments from a property path.
    ///     For scalar properties, all segments except the last are navigations.
    ///     For DTO/collection properties, all segments (including last) are navigations.
    /// </summary>
    private static void AddNavigationSegments(string path, bool lastSegmentIsNavigation, HashSet<string> navigations)
    {
        var parts = path.Split('.');
        if (parts.Length == 0)
            return;

        var limit = lastSegmentIsNavigation ? parts.Length : parts.Length - 1;

        for (var i = 1; i <= limit; i++)
            navigations.Add(string.Join(".", parts.Take(i)));
    }

    /// <summary>
    ///     Extracts the property path from a source expression like "entity.Customer?.Name".
    ///     Returns null for expressions that are not simple property paths.
    /// </summary>
    private static string? CleanEntityPath(string sourceExpression)
    {
        if (!sourceExpression.StartsWith("entity.", StringComparison.Ordinal))
            return null;

        var path = sourceExpression.Substring("entity.".Length);
        path = path.Replace("?.", ".").Replace("!.", ".");

        // Skip expressions containing operators, method calls, or string literals
        if (path.IndexOfAny([' ', '(', '+', '"']) >= 0)
            return null;

        return path;
    }

    private static string GetTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol is { IsRecord: true, IsValueType: true })
            return "record struct";
        if (symbol.IsRecord)
            return "record";
        if (symbol.IsValueType)
            return "struct";
        return "class";
    }
}
