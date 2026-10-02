using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Transforms;

/// <summary>
///     Property mapping analysis methods for MappingTransform.
/// </summary>
internal static partial class MappingTransform
{
    private static PropertyMappingModel AnalyzePropertyMapping(
        IPropertySymbol targetProp,
        INamedTypeSymbol sourceSymbol,
        ImmutableArray<IPropertySymbol> sourceProperties,
        Compilation compilation,
        int projectionMaxDepth = 5)
    {
        // Check for [MapIgnore] — [MapProperty] alongside it is a contradiction (PRAG0314; ignore wins).
        // Reading: a property ignored only for the write is still read.
        if (AttributeAnalyzer.IsIgnoredFor(targetProp, writing: false))
            return CreateIgnoredProperty(targetProp) with
            {
                HasConflictingAttributes = AttributeAnalyzer.GetMapPropertyAttribute(targetProp) is not null
            };

        // Check for [MapProperty]
        var mapPropertyAttr = AttributeAnalyzer.GetMapPropertyAttribute(targetProp);
        // Check for [MapConverter] - always check, even with [MapProperty]. Contract validity flows
        // into PRAG0305/PRAG0306.
        var converterInfo = AttributeAnalyzer.GetConverterInfo(targetProp);
        var converterAttr = converterInfo.TypeName;

        // [MapCondition]: predicate gating the mapping (static bool on the DTO taking the source).
        var (conditionMethod, conditionInvalid) = GetConditionInfo(targetProp, sourceSymbol);

        if (mapPropertyAttr is not null)
        {
            var explicitMapping = CreateExplicitMapping(targetProp, sourceSymbol, mapPropertyAttr, converterAttr);
            var (explicitRead, explicitBody) =
                ProjectedTerminal(sourceSymbol, explicitMapping.SourceExpression, compilation);
            return explicitMapping with
            {
                ConverterMissingInterface = converterInfo.MissingInterface,
                ConverterMissingParameterlessCtor = converterInfo.MissingCtor,
                ConditionMethod = conditionMethod,
                ConditionMethodInvalid = conditionInvalid,
                ConditionProjectionBody = conditionInvalid || conditionMethod is null
                    ? null
                    : MapConditionBody.Of(targetProp.ContainingType, conditionMethod, compilation, "entity"),
                ProjectionSourceExpression = explicitMapping.ProjectionSourceExpression
                    ?? ProjectedSource(sourceProperties, explicitMapping.SourceExpression, compilation),
                ProjectableRead = explicitRead,
                ProjectableReadBody = explicitBody,
                ProjectableNavigations = ProjectableNavigations(sourceSymbol, explicitMapping.SourceExpression, compilation)
            };
        }

        // Try resolution strategies in priority order
        var (resolution, sourceExpr, sourceType, sourceNullable, sourceIsEnum, ambiguous) =
            ResolvePropertyMapping(targetProp.Name, sourceSymbol, sourceProperties);

        var collectionInfo = CollectionAnalyzer.AnalyzeCollectionType(targetProp.Type);
        var nestedInfo = NestedDtoAnalyzer.AnalyzeNestedDto(targetProp.Type, collectionInfo);

        // A nested DTO over the same row: nothing on the source carries the name, and the DTO
        // maps from the source itself — a group of the row's own columns ("the decision: by whom, when,
        // why"). Its source is the row, which is always there. Over another entity it stays PRAG0303.
        var isSameRow = resolution == MappingResolution.None && nestedInfo.IsNested
                        && TypeAnalyzer.UnwrapNullable(targetProp.Type) is INamedTypeSymbol sameRowDto
                        && MapFromSourceOf(sameRowDto) is { } sameRowSource
                        && IsSameOrDerivedFrom(sourceSymbol, sameRowSource);
        if (isSameRow)
        {
            resolution = MappingResolution.DirectMatch;
            sourceExpr = "entity";
            sourceNullable = false;
        }

        // PRAG0315: a nested DTO whose [MapFrom<T>] is unrelated to the actual navigation type would
        // generate a FromEntity call that doesn't compile. Symbol-based check (inheritance-aware).
        string? nestedMismatchSource = null;
        if (nestedInfo.IsNested)
        {
            var directMatch = sourceProperties.FirstOrDefault(p =>
                string.Equals(p.Name, targetProp.Name, StringComparison.OrdinalIgnoreCase));
            if (directMatch is not null)
                nestedMismatchSource = NestedDtoAnalyzer.GetMapFromSourceMismatch(targetProp.Type, directMatch.Type);
        }

        // Class-level [MapConverter]: applies to convention-mapped scalars whose source→target types
        // match the converter signature (property-level converters take precedence).
        if (converterAttr is null && resolution != MappingResolution.None
            && !nestedInfo.IsNested && collectionInfo.Kind == CollectionKind.None && !collectionInfo.IsDictionary)
        {
            var typeLevel = AttributeAnalyzer.GetTypeLevelConverterInfo(
                targetProp.ContainingType, sourceType, targetProp.Type.ToDisplayString());
            if (typeLevel.TypeName is not null)
            {
                converterInfo = typeLevel;
                converterAttr = typeLevel.TypeName;
            }
        }

        // Detect type conversion (only if no explicit converter)
        var targetIsEnum = TypeConversionHelper.IsEnumType(targetProp.Type);
        var conversion = converterAttr is null
            ? TypeConversionHelper.DetectConversion(sourceType, targetProp.Type.ToDisplayString(), sourceIsEnum,
                targetIsEnum)
            : ConversionKind.None;

        // Enum → DIFFERENT enum type: by-name switch, validated at compile time (every source member
        // must exist on the target — PRAG0328 otherwise). Non-nullable direct matches only.
        var enumMembers = ImmutableArray<string>.Empty;
        string? enumMismatch = null;
        if (converterAttr is null && conversion == ConversionKind.None
            && resolution == MappingResolution.DirectMatch
            && targetProp.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } targetEnum)
        {
            var sourceMatch = sourceProperties.FirstOrDefault(p =>
                string.Equals(p.Name, targetProp.Name, StringComparison.OrdinalIgnoreCase));
            if (sourceMatch?.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } sourceEnum
                && !SymbolEqualityComparer.Default.Equals(sourceEnum, targetEnum))
            {
                // By value is a cast, so there is nothing to check and nothing to enumerate: any
                // number converts. The author asked for someone else's numbering and gets it,
                // including the part where reordering either enum changes what pairs with what.
                if (AttributeAnalyzer.MatchesEnumsByValue(targetProp))
                {
                    conversion = ConversionKind.EnumToEnumByValue;
                }
                else
                {
                    var targetMembers = new HashSet<string>(
                        targetEnum.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name), StringComparer.Ordinal);
                    var sourceMembers = sourceEnum.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name).ToImmutableArray();

                    enumMismatch = sourceMembers.FirstOrDefault(m => !targetMembers.Contains(m));
                    if (enumMismatch is null)
                    {
                        conversion = ConversionKind.EnumToEnum;
                        enumMembers = sourceMembers;
                    }
                    else
                    {
                        resolution = MappingResolution.None; // skip: the by-name switch would not compile
                    }
                }
            }
        }

        // Extract nested projection mappings for EF Core inlining.
        // droppedNested/droppedElement flag nested DTO mapping forms (converter, concat, format,
        // flatten) that the inline initializer cannot represent (PRAG0326).
        // cappedNested/cappedElement flag depth-cap truncation (PRAG0327).
        var droppedNested = false;
        var droppedElement = false;
        var cappedNested = false;
        var cappedElement = false;
        var nestedProjectionMappings = nestedInfo.IsNested
            ? ExtractNestedProjectionMappings(targetProp.Type, compilation, out droppedNested, out cappedNested, maxDepth: projectionMaxDepth)
            : ImmutableArray<NestedPropertyMapping>.Empty;
        var elementProjectionMappings = nestedInfo.IsElementDto && collectionInfo.ElementTypeSymbol is not null
            ? ExtractNestedProjectionMappings(collectionInfo.ElementTypeSymbol, compilation, out droppedElement, out cappedElement, maxDepth: projectionMaxDepth)
            : ImmutableArray<NestedPropertyMapping>.Empty;

        var (projectableRead, projectableReadBody) = resolution == MappingResolution.Flattening
            ? ProjectedTerminal(sourceSymbol, sourceExpr, compilation)
            : (null, null);

        return new PropertyMappingModel
        {
            Location = PropertyLocation(targetProp),
            PropertyName = targetProp.Name,
            PropertyType = targetProp.Type.ToDisplayString(),
            PropertyFullTypeName = targetProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            // Whichever end is the enum carries the wire names: the DTO's own type when the entity
            // holds a string, the entity's when the DTO exposes one.
            EnumAliases = AttributeAnalyzer.ReadEnumAliases(
                targetIsEnum
                    ? targetProp.Type
                    : sourceProperties
                        .FirstOrDefault(p => string.Equals(
                            p.Name, targetProp.Name, StringComparison.OrdinalIgnoreCase))?.Type),
            IsNullable = targetProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
            IsRequired = targetProp.IsRequired,
            IsInitOnly = PropertyAnalyzer.IsInitOnly(targetProp),
            HasPrivateSetter = PropertyAnalyzer.HasPrivateSetter(targetProp),
            Resolution = resolution,
            SourceExpression = sourceExpr,
            ProjectionSourceExpression = resolution == MappingResolution.DirectMatch
                ? ProjectedSource(sourceProperties, sourceExpr, compilation)
                : null,
            ProjectableRead = projectableRead,
            ProjectableReadBody = projectableReadBody,
            ProjectableNavigations = resolution is MappingResolution.DirectMatch or MappingResolution.Flattening
                ? ProjectableNavigations(sourceSymbol, sourceExpr, compilation)
                : ImmutableArray<string>.Empty,
            SourcePropertyType = sourceType,
            SourceIsNullable = sourceNullable,
            SourceIsSameRowValue = ReadsAValueInTheEntitysOwnRow(sourceExpr, sourceSymbol),
            SourceIsEnum = sourceIsEnum,
            TargetIsEnum = targetIsEnum,
            Conversion = conversion,
            ConverterType = converterAttr,
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
            IsSqlTranslatable = SqlTranslatableAnalyzer.IsSqlTranslatable(targetProp, converterAttr, mapPropertyAttr),
            NestedProjectionMappings = nestedProjectionMappings,
            ElementProjectionMappings = elementProjectionMappings,
            HasDroppedNestedProjectionMappings = droppedNested || droppedElement,
            ProjectionDepthCapped = cappedNested || cappedElement,
            AmbiguousWithConvention = ambiguous,
            NestedDtoMismatchSource = nestedMismatchSource,
            ConverterMissingInterface = converterInfo.MissingInterface,
            ConverterMissingParameterlessCtor = converterInfo.MissingCtor,
            EnumToEnumMembers = enumMembers,
            EnumMemberMismatch = enumMismatch,
            ConditionMethod = conditionMethod,
            ConditionMethodInvalid = conditionInvalid,
            ConditionProjectionBody = conditionInvalid || conditionMethod is null
                ? null
                : MapConditionBody.Of(targetProp.ContainingType, conditionMethod, compilation, "entity"),
            IsSameRow = isSameRow,
            // A reference type only: a struct's default is a value, and it was never assigned null.
            IsDeclaredNonNull = targetProp.Type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.NotAnnotated }
        };
    }

    /// <summary>
    ///     Reads [MapCondition] and validates the predicate: a static bool method on the DTO whose
    ///     single parameter is the source type (or a base of it). Invalid shape → PRAG0329.
    /// </summary>
    private static (string? Method, bool Invalid) GetConditionInfo(
        IPropertySymbol targetProp,
        INamedTypeSymbol sourceSymbol)
    {
        var attr = targetProp.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "Pragmatic.Mapping.Attributes.MapConditionAttribute");
        if (attr is null || attr.ConstructorArguments.Length == 0
            || attr.ConstructorArguments[0].Value is not string methodName || methodName.Length == 0)
            return (null, false);

        var valid = targetProp.ContainingType.GetMembers(methodName).OfType<IMethodSymbol>().Any(m =>
            m is { IsStatic: true, ReturnType.SpecialType: SpecialType.System_Boolean, Parameters.Length: 1 }
            && IsSameOrDerivedFrom(sourceSymbol, m.Parameters[0].Type));

        return (methodName, !valid);
    }

    /// <summary>True when the type is a <c>Nullable&lt;T&gt;</c> (a nullable value type such as <c>int?</c>).</summary>
    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>True when <paramref name="type"/> is <paramref name="candidateBase"/> or derives from it.</summary>
    private static bool IsSameOrDerivedFrom(ITypeSymbol type, ITypeSymbol candidateBase)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, candidateBase))
                return true;
        return false;
    }

    /// <summary>
    ///     The conversion the write path needs, with the roles inverted: DTO in, entity out.
    /// </summary>
    /// <remarks>
    ///     Two different enums are a cast only when the author asked for one — <c>[MapEnum(ByValue)]</c>.
    ///     Otherwise they pair by name, which is what <c>DetectConversion</c> does not decide and the
    ///     read side works out with the member lists.
    /// </remarks>
    private static ConversionKind WriteConversion(IPropertySymbol dtoProp, IPropertySymbol entityProp)
    {
        var sourceIsEnum = TypeConversionHelper.IsEnumType(dtoProp.Type);
        var targetIsEnum = TypeConversionHelper.IsEnumType(entityProp.Type);

        if (sourceIsEnum && targetIsEnum
            && !SymbolEqualityComparer.Default.Equals(dtoProp.Type, entityProp.Type)
            && AttributeAnalyzer.MatchesEnumsByValue(dtoProp))
            return ConversionKind.EnumToEnumByValue;

        return TypeConversionHelper.DetectConversion(
            dtoProp.Type.ToDisplayString(),
            entityProp.Type.ToDisplayString(),
            sourceIsEnum,
            targetIsEnum);
    }

    /// <summary>
    ///     The entity a navigation holds, for a collection navigation named by <c>[LinkIds]</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Only navigations the entity <b>declares</b> are visible here. A navigation that another
    ///     generator will add — everything a <c>[Relation.*]</c> produces — is not a symbol during
    ///     this pass, so the element type is read from <c>TraitPropertyResolver</c>'s prediction of
    ///     what will exist, which is the same channel the rest of this transform already uses.
    /// </remarks>
    private static string? LinkTargetEntity(INamedTypeSymbol entitySymbol, string navigation)
    {
        var declared = entitySymbol.GetMembers(navigation).OfType<IPropertySymbol>().FirstOrDefault();

        var navigationType = declared?.Type;
        if (navigationType is null)
        {
            foreach (var predicted in Core.TraitPropertyResolver.GetRelationNavigations(entitySymbol))
            {
                if (!string.Equals(predicted.Name, navigation, StringComparison.Ordinal))
                    continue;

                navigationType = predicted.TypeSymbol;
                break;
            }
        }

        if (navigationType is null)
            return null;

        var element = CollectionAnalyzer.AnalyzeCollectionType(navigationType).ElementTypeSymbol
                      ?? navigationType;
        return element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static PropertyMappingModel AnalyzePropertyMappingForMapTo(
        IPropertySymbol dtoProp,
        INamedTypeSymbol entitySymbol,
        ImmutableArray<IPropertySymbol> entityProperties,
        Compilation compilation)
    {
        // Check for [MapIgnore] — [MapProperty] alongside it is a contradiction (PRAG0314; ignore wins).
        // Writing: a property ignored only for the read is still written.
        if (AttributeAnalyzer.IsIgnoredFor(dtoProp, writing: true))
            return CreateIgnoredProperty(dtoProp) with
            {
                HasConflictingAttributes = AttributeAnalyzer.GetMapPropertyAttribute(dtoProp) is not null
            };

        // Check for [MapProperty] - read full details including Target
        var mapPropertyAttr = AttributeAnalyzer.GetMapPropertyAttribute(dtoProp);

        // Resolve [MapConverter] for the write path so ToEntity/ApplyTo can emit
        // ConvertBack(...) — the IValueConverter contract documents ConvertBack for DTO→entity.
        // Contract validity flows into PRAG0305/PRAG0306.
        var converterInfo = AttributeAnalyzer.GetConverterInfo(dtoProp);
        var converterType = converterInfo.TypeName;

        // [LinkIds("Nav")]: a list of keys choosing which rows the navigation points at. Resolved
        // here because the element entity comes from the ENTITY's navigation, not from the DTO — the
        // caller sends keys, so the DTO's own type says nothing about what is on the other side.
        var linkIds = LinkIdsAnalyzer.Read(dtoProp);
        var linkEntity = linkIds is { } link ? LinkTargetEntity(entitySymbol, link.Navigation) : null;

        // Check if this is an ID property
        var isIdProperty = PropertyAnalyzer.IsIdProperty(dtoProp.Name, entitySymbol.Name);

        // Check for explicit [MapProperty] to force ID inclusion
        var forceIncludeId = isIdProperty && mapPropertyAttr is not null;

        // Try to find matching entity property
        var entityProp = entityProperties.FirstOrDefault(p =>
            string.Equals(p.Name, dtoProp.Name, StringComparison.OrdinalIgnoreCase));

        // Fallback: check trait-generated properties (e.g., Id, CreatedAt from [Entity]/[Auditable])
        string? traitPropertyType = null;
        var traitIsNullable = false;

        // The entity on the other side of a generated collection navigation. A navigation the
        // persistence generator writes is not a property symbol during this pass, so the collection
        // analyzer cannot reach the child through entityProp and must be handed the type directly.
        ITypeSymbol? traitCollectionElement = null;

        if (entityProp is null)
        {
            var traitProperties = TraitPropertyResolver.GetGeneratedProperties(entitySymbol);
            foreach (var vp in traitProperties)
            {
                if (string.Equals(vp.Name, dtoProp.Name, StringComparison.OrdinalIgnoreCase))
                {
                    traitIsNullable = vp.TypeFullName.EndsWith("?");
                    traitPropertyType = traitIsNullable ? vp.TypeFullName.TrimEnd('?') : vp.TypeFullName;
                    if (vp.IsCollection)
                        traitCollectionElement = vp.TypeSymbol;
                    break;
                }
            }
        }

        // A property the invoker writes — [FromClock], [FromCurrentUser] — is written to the entity when
        // the entity has a member of its name, and is otherwise no input to map: the operation's body
        // reads it. So is an input of a row it preloads — the key of a [LoadEntity] or [LoadEntities], a
        // parameter of the rule one reads by. The same rule the mutation's own auto-map applies, so the two
        // do not disagree.
        if (entityProp is null && traitPropertyType is null
            && (Core.InvokerBinding.IsBound(dtoProp) || Core.LoadKey.IsNamedByALoad(dtoProp, compilation)))
            return CreateIgnoredProperty(dtoProp);

        // Analyze collection type (DTO property is the source in MapTo)
        var collectionInfo = CollectionAnalyzer.AnalyzeCollectionType(dtoProp.Type);

        // The write path must materialize what the ENTITY declares (List vs HashSet vs Immutable...),
        // not what the DTO declares.
        var targetCollectionKind = entityProp is not null
            ? CollectionAnalyzer.AnalyzeCollectionType(entityProp.Type).Kind
            : CollectionKind.None;

        // Analyze nested DTO with [MapTo] attribute (for ToEntity direction)
        var nestedInfo = NestedDtoAnalyzer.AnalyzeNestedDto(dtoProp.Type, collectionInfo, MappingDirection.ToEntity);

        // A child that is a mutation is built from the entity's factory, not from ToEntity.
        var childSymbol = nestedInfo.IsElementDto
            ? TypeAnalyzer.UnwrapNullable(collectionInfo.ElementTypeSymbol) as INamedTypeSymbol
            : nestedInfo.IsNested
                ? TypeAnalyzer.UnwrapNullable(dtoProp.Type) as INamedTypeSymbol
                : null;
        var childMutationEntity = childSymbol is null ? null : Analysis.MutationAnalyzer.EntityOf(childSymbol);


        // Class-level [MapConverter] on the write path: ConvertBack(TTarget)→TSource, so the DTO
        // property must be TTarget and the entity property TSource.
        if (converterType is null && entityProp is not null
            && !nestedInfo.IsNested && collectionInfo.Kind == CollectionKind.None && !collectionInfo.IsDictionary)
        {
            var typeLevel = AttributeAnalyzer.GetTypeLevelConverterInfo(
                dtoProp.ContainingType, entityProp.Type.ToDisplayString(), dtoProp.Type.ToDisplayString());
            if (typeLevel.TypeName is not null)
            {
                converterInfo = typeLevel;
                converterType = typeLevel.TypeName;
            }
        }

        // Determine resolution - if Target is specified, treat as Explicit even without direct match
        var hasTargetPath = mapPropertyAttr?.Target is not null;

        // Validate the Target path against the entity shape: every segment must exist. Intermediate
        // navigations are captured so the templates can null-guard them (no NRE on a null navigation).
        var targetIntermediates = ImmutableArray<string>.Empty;
        var targetConstructible = true;
        var targetCrossesEntity = false;
        string? targetInvalidSegment = null;
        var correctedTargetPath = mapPropertyAttr?.Target;
        if (hasTargetPath)
            (correctedTargetPath, targetIntermediates, targetConstructible, targetCrossesEntity, targetInvalidSegment) =
                AnalyzeTargetPath(entitySymbol, mapPropertyAttr!.Target!);

        // [LinkIds] IS the destination: the navigation it names, not a property of the same name on
        // the entity. Leaving the resolution at None would make every downstream filter that asks "is
        // this mapped?" answer no — the property dropped from the write body, silently, and PRAG0303
        // reporting it as unmatched. It is explicit configuration exactly like [MapProperty(Target)].
        // Which entity property is really written. With [MapProperty(Target = "X")] it is not the one
        // named like the DTO property — which usually does not exist — but X, and X's accessors decide
        // the shape of the write. Asking entityProp would answer «public setter» for want of a symbol,
        // and the template would assign directly to a property with a private set: CS0272 in a generated
        // file. Only a one-segment path: with A.B the target is on another type, which the template
        // already reaches by navigation, where Set{A.B} would not exist.
        // ⚠️ A target on a member another generator produces is not a symbol here, so it stays out:
        // writeTarget is null and the defaults apply.
        var writeTarget = hasTargetPath && targetIntermediates.Length == 0 && correctedTargetPath is not null
            ? entityProperties.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, correctedTargetPath, StringComparison.Ordinal))
            : entityProp;

        var resolution = linkIds is not null ? MappingResolution.Explicit
            : targetInvalidSegment is not null ? MappingResolution.None
            : hasTargetPath ? MappingResolution.Explicit
            : entityProp is not null ? MappingResolution.DirectMatch
            : traitPropertyType is not null ? MappingResolution.DirectMatch
            : MappingResolution.None;

        // A DTO nullable value type (int?) assigned to a non-nullable value-type entity property (int)
        // must be unwrapped in ToEntity/ApplyTo — otherwise the generated assignment is a CS0266.
        // A property another feature generates — a relation's key — has no symbol during this pass,
        // but its type is known above: a nullable value type on the DTO can only land on it as the
        // same type, so a non-nullable generated property needs the same unwrap.
        var needsNullableValueUnwrap = IsNullableValueType(dtoProp.Type)
            && (entityProp is not null
                ? entityProp.Type.IsValueType && !IsNullableValueType(entityProp.Type)
                : traitPropertyType is not null && !traitIsNullable);

        // How a collection of DTOs is written back, and by what its elements are matched. Only for a
        // collection of DTOs: a collection of scalars is a value and is copied whole.
        //
        // Which overload matters. Reaching the child through entityProp answers null for a generated
        // navigation, and a null child made the analyzer report NoKey — which the template renders as
        // the constant selector `d => 0, e => 0`. Two children then carry the same key and the write
        // throws DuplicateMappingKeyException at runtime instead of matching them. The depth-2 case in
        // examples/conformance is what measured it: Order.Lines is written by a mutation, whose own
        // template resolves the key, while OrderLine.Allocations is written by this one.
        var elementDtoSymbol = TypeAnalyzer.UnwrapNullable(collectionInfo.ElementTypeSymbol) as INamedTypeSymbol;

        // ⚠️ The child must write the entity that navigation holds. A List<WriteDiscountMutation> on a
        // navigation of LineItem is the author's mistake, and Actions reports it as PRAG0443; emitting the
        // call regardless would be a CS0411 inside a generated file, the defect reaching the author in the
        // one form they cannot open. Here the write is simply not emitted, and there is one message.
        var navigationElement = entityProp is not null
            ? CollectionAnalyzer.AnalyzeCollectionType(entityProp.Type).ElementTypeSymbol
            : traitCollectionElement;
        var childMutationTarget = MutationAnalyzer.EntityOf(elementDtoSymbol);
        var childWritesSomethingElse =
            childMutationTarget is { } childEntity
            && navigationElement is not null
            && !SymbolEqualityComparer.Default.Equals(
                childEntity, TypeAnalyzer.UnwrapNullable(navigationElement));

        // And the child must have allowed it. Without [PartOf<Parent>] Actions refuses the pair with
        // PRAG0436, and a body that wrote regardless would make the parent a shortcut around the child's
        // rules — a fail-open shape.
        var childDidNotAllowIt =
            childMutationTarget is not null
            && !MutationAnalyzer.IsPartOf(childMutationTarget, entitySymbol);

        var collectionWrite = nestedInfo.IsElementDto && !childWritesSomethingElse && !childDidNotAllowIt
            ? entityProp is not null
                ? CollectionWriteAnalyzer.Analyze(dtoProp, elementDtoSymbol, entityProp)
                : CollectionWriteAnalyzer.Analyze(dtoProp, elementDtoSymbol, traitCollectionElement)
            : null;

        return new PropertyMappingModel
        {
            Location = PropertyLocation(dtoProp),
            PropertyName = dtoProp.Name,
            PropertyType = dtoProp.Type.ToDisplayString(),
            IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
            NeedsNullableValueUnwrap = needsNullableValueUnwrap,
            IsRequired = dtoProp.IsRequired,
            IsInitOnly = PropertyAnalyzer.IsInitOnly(dtoProp),
            Resolution = resolution,
            SourceExpression = entityProp is not null || hasTargetPath || traitPropertyType is not null ? dtoProp.Name : null,
            SourcePropertyType = entityProp?.Type.ToDisplayString() ?? traitPropertyType ?? (hasTargetPath ? dtoProp.Type.ToDisplayString() : null),
            SourceIsNullable = entityProp?.Type.NullableAnnotation == NullableAnnotation.Annotated || traitIsNullable,
            CollectionWrite = collectionWrite,
            TargetPath = correctedTargetPath,
            TargetPathIntermediates = targetIntermediates,
            TargetIntermediatesConstructible = targetConstructible,
            TargetPathCrossesEntity = targetCrossesEntity,
            // An entity that keeps its state private is written through the generated Set{Name};
            // assigned directly, it would not compile. The foreign keys a [Relation.*] generates are the
            // same shape — `private set` plus Set{Fk} — and are not on the symbol during this pass, so
            // they are asked of the predictor: assigned directly, they would not compile either.
            TargetHasNonPublicSetter = (writeTarget is { SetMethod: not null }
                                        && writeTarget.SetMethod.DeclaredAccessibility != Accessibility.Public)
                                       || (writeTarget is null
                                           && TraitPropertyResolver.GetRelationForeignKeys(entitySymbol)
                                               .Any(fk => string.Equals(fk.Name, dtoProp.Name, StringComparison.OrdinalIgnoreCase))),
            // ⚠️ An init-only target can be written only inside an object initializer. It decides the
            // shape of the whole write: building through the factory means assigning afterwards, and
            // afterwards is exactly when `init` refuses.
            TargetIsInitOnly = writeTarget is { SetMethod.IsInitOnly: true },
            // A computed property is not a target: the write path emitted the assignment and the
            // consumer got CS0200 in generated code. Skipped and reported, not skipped in silence.
            TargetIsReadOnly = writeTarget is { SetMethod: null, IsIndexer: false },
            EnumOnUnknown = AttributeAnalyzer.ReadEnumOnUnknown(dtoProp),
            EnumMatchesByValue = AttributeAnalyzer.MatchesEnumsByValue(dtoProp),
            EnumAliases = AttributeAnalyzer.ReadEnumAliases(
                TypeConversionHelper.IsEnumType(dtoProp.Type) ? dtoProp.Type : entityProp?.Type),
            TargetFullTypeName = entityProp?.Type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat),
            // ⚠️ The write side computes its own conversion. The roles invert here — the DTO
            // property is the source and the entity property the target — and without this a
            // `string` mapped onto an enum column would emit `entity.X = this.X`, which is CS0029
            // inside a generated file. An explicit [MapConverter] would still work, so the gap would
            // show only on the conversions the framework is supposed to do by itself.
            Conversion = converterType is null && entityProp is not null
                ? WriteConversion(dtoProp, entityProp)
                : ConversionKind.None,
            ReferenceStrategy = ReferenceWriteAnalyzer.ReadStrategy(dtoProp),
            ChildIsMutation = childMutationEntity is not null,
            ChildEntityFullTypeName = childMutationEntity?.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat),
            LinkNavigation = linkIds?.Navigation,
            LinkKey = linkIds?.Key ?? "PersistenceId",
            LinkStrategy = linkIds?.Strategy ?? "Sync",
            LinkEntityFullTypeName = linkEntity,
            TargetPathInvalidSegment = targetInvalidSegment,
            ConverterMissingInterface = converterInfo.MissingInterface,
            ConverterMissingParameterlessCtor = converterInfo.MissingCtor,
            ConverterType = converterType,
            IsIdProperty = isIdProperty,
            ForceIncludeId = forceIncludeId,
            // Nested DTO info for ToEntity
            CollectionKind = collectionInfo.Kind,
            TargetCollectionKind = targetCollectionKind,
            ElementType = collectionInfo.ElementType,
            IsElementSimple = collectionInfo.IsElementSimple,
            IsNestedDto = nestedInfo.IsNested,
            NestedDtoType = nestedInfo.DtoType,
            IsElementDto = nestedInfo.IsElementDto,
            ElementDtoType = nestedInfo.ElementDtoType
        };
    }

    /// <summary>
    ///     Walks a <c>[MapProperty(Target = "...")]</c> path on the entity: validates every segment
    ///     exists (else returns the offending segment for PRAG0302), corrects casing to the declared
    ///     property names, and captures intermediate navigation prefixes with their constructibility
    ///     so the write templates can emit <c>??= new()</c> / null-guards instead of NRE-prone raw
    ///     dotted assignments. Value-type intermediates keep the raw form (pre-existing behavior:
    ///     assigning through a struct property is a compile error anyway).
    /// </summary>
    private static (string CorrectedPath, ImmutableArray<string> Intermediates, bool Constructible, bool CrossesEntity, string? InvalidSegment)
        AnalyzeTargetPath(INamedTypeSymbol entitySymbol, string targetPath)
    {
        var segments = targetPath.Split('.');
        var intermediates = ImmutableArray.CreateBuilder<string>();
        var constructible = true;
        var crossesEntity = false;
        ITypeSymbol currentType = entitySymbol;
        var prefix = "";

        for (var i = 0; i < segments.Length; i++)
        {
            var prop = currentType is INamedTypeSymbol named
                ? PropertyAnalyzer.GetAllProperties(named)
                    .FirstOrDefault(p => string.Equals(p.Name, segments[i], StringComparison.OrdinalIgnoreCase))
                : null;

            if (prop is null)
                return (targetPath, ImmutableArray<string>.Empty, true, false, segments[i]);

            prefix = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";

            if (i < segments.Length - 1)
            {
                // A value-type navigation can't be null-guarded nor ??=-initialized; bail out to the
                // raw assignment (the compiler rejects member-assignment through a struct property).
                if (prop.Type.IsValueType)
                    return (prefix + "." + string.Join(".", segments.Skip(i + 1)), ImmutableArray<string>.Empty, true, false, null);

                intermediates.Add(prefix);
                if (prop.Type is not INamedTypeSymbol intermediateType
                    || !intermediateType.InstanceConstructors.Any(c =>
                        c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
                    constructible = false;

                if (IsEntity(prop.Type))
                    crossesEntity = true;

                currentType = prop.Type;
            }
        }

        return (prefix, intermediates.ToImmutable(), constructible, crossesEntity, null);
    }

    /// <summary>Whether the type is a persisted entity — a row with an identity of its own.</summary>
    private static bool IsEntity(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "EntityAttribute")
                    return true;
            }
        }

        return false;
    }
}
