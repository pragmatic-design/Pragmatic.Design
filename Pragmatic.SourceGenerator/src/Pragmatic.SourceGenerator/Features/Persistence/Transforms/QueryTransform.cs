using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform logic for [Query] attribute.
/// </summary>
internal static class QueryTransform
{
    private const string FilterAttributeName = "Pragmatic.Persistence.Query.Attributes.FilterAttribute";
    private const string SortAttributeName = "Pragmatic.Persistence.Query.Attributes.SortAttribute";
    private const string JoinAttributeName = "Pragmatic.Persistence.Query.Attributes.JoinAttribute`1";
    private const string ComplexFilterAttributeName = "Pragmatic.Persistence.Query.Attributes.ComplexFilterAttribute";
    private const string BindSpecificationAttributeName = "Pragmatic.Persistence.Query.Attributes.BindSpecificationAttribute";
    private const string SpecificationTypeName = "Pragmatic.Specification.Specification";
    private const string GridFilterRequestTypeName = "Pragmatic.Persistence.Query.Adapters.GridFilterRequest";
    private const string GenerateGridBridgeAttributeName =
        "Pragmatic.Persistence.Query.Attributes.GenerateGridBridgeAttribute";

    // Two attributes a query property may carry and no query template reads. Named here so the
    // feature can report them rather than let the property name fall through as an entity column.
    private const string FilterGroupAttributeName = "Pragmatic.Persistence.Query.Attributes.FilterGroupAttribute";

    /// <summary>
    ///     Transforms [Query] attribute to QueryModel.
    /// </summary>
    public static QueryModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        // Get entity and result types from attribute
        var attribute = context.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is not INamedTypeSymbol attrType)
            return null;

        // Handle both Query<TEntity, TResult> and Query<TEntity> (where TResult = TEntity)
        INamedTypeSymbol? entityType;
        INamedTypeSymbol? resultType;

        if (attrType.TypeArguments.Length == 2)
        {
            entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
            resultType = attrType.TypeArguments[1] as INamedTypeSymbol;
        }
        else if (attrType.TypeArguments.Length == 1)
        {
            entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
            resultType = entityType; // TResult = TEntity
        }
        else
        {
            return null;
        }

        if (entityType is null || resultType is null)
            return null;

        // Not `return null` when the class is not partial: that answer is indistinguishable from "this
        // node is not mine", and the pipeline drops it before anything can report. The model carries it
        // and the feature skips the type; PRAG0712 is the companion analyzer's, on the declaration.
        var isPartial = IsPartialType(context.TargetNode);

        // Check for base query class
        var baseQuery = GetBaseQueryType(targetSymbol);

        // Extract properties
        var properties = ExtractProperties(targetSymbol, IdIsTheGeneratedAlias(entityType), ct);

        // Paged = true writes the two properties the author would otherwise repeat. They are added to
        // the model, not only to the emitted file, because every downstream reading of "does this page"
        // goes through the properties: the interface the query implements, the executor overload the
        // invoker calls, and the values the endpoint binds from the query string.
        var pagingIsRequested = Core.QueryShapeReader.PagingIsRequested(attribute);
        var declaresPaging = Core.QueryShapeReader.DeclaresPaging(targetSymbol);
        var generatesPaging = pagingIsRequested && !declaresPaging;

        if (generatesPaging)
        {
            properties = properties
                .Add(new QueryPropertyModel { PropertyName = "Page", PropertyType = "int", IsPageProperty = true })
                .Add(new QueryPropertyModel
                {
                    PropertyName = "PageSize", PropertyType = "int", IsPageSizeProperty = true
                });
        }

        // Extract joins
        var declaredJoins = ExtractJoins(targetSymbol, entityType, ct);
        var joins = declaredJoins.Select(j => j.Model).ToImmutableArray();

        // Where each property of the result reads its value, for the joins that generate a step.
        // Only those: an unresolved key or a join type EF Core cannot translate is refused, and
        // mapping the result against a join that will not be generated would produce a step naming a
        // source that never arrives.
        var generatedKeyJoins = declaredJoins
            .Where(j => j.Model.IsResolvedKeyJoin && j.Model.IsGeneratableJoinType)
            .ToImmutableArray();

        var (joinedResultProperties, unresolvedResultProperties) =
            generatedKeyJoins.Length > 0 && !SymbolEqualityComparer.Default.Equals(entityType, resultType)
                ? KeyJoinProjection.Resolve(entityType, resultType, generatedKeyJoins)
                : (ImmutableArray<JoinedResultPropertyModel>.Empty, ImmutableArray<string>.Empty);

        // Extract complex filters
        var complexFilters = ExtractComplexFilters(targetSymbol, ct);

        var (declaredPermissions, unresolvedPermissions, requiresAll) = ReadPermissions(targetSymbol, context.SemanticModel.Compilation);

        // [EagerLoad] is read once, by the reader the mutation uses: a path that names no navigation is reported and
        // not emitted — it was an EF Core exception at the first request.
        var (eagerLoadPaths, eagerLoadProblems) = Actions.Transforms.IncludePaths.EagerLoads(targetSymbol, entityType);

        return new QueryModel
        {
            Namespace = targetSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = targetSymbol.Name,
            Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            DeclaredSubBoundaryName = Actions.Transforms.SubBoundaryTransform.Declared(targetSymbol),
            SubBoundaryName = Actions.Transforms.SubBoundaryTransform.Usable(
                Actions.Transforms.SubBoundaryTransform.Declared(targetSymbol)),
            SubBoundaryDescription = Actions.Transforms.SubBoundaryTransform.DeclaredDescription(targetSymbol),
            TypeKind = GetTypeKind(targetSymbol),
            IsRecord = targetSymbol.IsRecord,
            EntityTypeFullName = entityType.ToDisplayString(),
            EntityTypeName = entityType.Name,
            ResultTypeFullName = resultType.ToDisplayString(),
            ResultTypeName = resultType.Name,
            // The projected type does not resolve: its display is the bare name, and writing that back
            // out puts a CS0400 in every file this query produces.
            UnresolvedResultType = resultType.TypeKind == TypeKind.Error ? resultType.ToDisplayString() : null,
            Properties = properties,
            Joins = joins,
            JoinedResultProperties = joinedResultProperties,
            UnresolvedResultProperties = unresolvedResultProperties,
            IsPartial = isPartial,
            ComplexFilters = complexFilters,
            Specifications = ExtractSpecifications(targetSymbol, ct),
            SpecificationInputs = ExtractSpecificationInputs(targetSymbol),
            CurrentUserBindings = CurrentUserBindingTransform.Extract(targetSymbol, context.SemanticModel),
            ClockBindings = ClockBindingTransform.Extract(targetSymbol),
            GridRequests = ExtractGridRequests(targetSymbol, ct),
            EntityDeclaresGridBridge = entityType.GetAttributes().Any(
                a => a.AttributeClass?.ToDisplayString() == GenerateGridBridgeAttributeName),
            BoundaryTypeName = Core.QueryShapeReader.BoundaryOf(entityType),
            // Read through the same two parsers the endpoint transform uses, so the invoker and the
            // endpoint agree on the source they build.
            Strategy = QueryStrategyParser.Read(targetSymbol),
            HasFilterOverrides = Actions.Transforms.FilterOverrideParser.Parse(targetSymbol)?.HasOverrides == true,
            IsSingle = Core.QueryShapeReader.IsSingle(attribute),
            MapsInMemory = Core.QueryShapeReader.MapsInMemory(attribute),
            // The result declares the grouping; the query only has to hand the set over.
            ResultIsAggregateView = resultType.GetAttributes().Any(
                a => a.AttributeClass?.Name == "QueryViewAttribute"),
            // Read with the endpoint transform's own readers: the posture is one rule, and two
            // readings of "does this opt out" is how the two doors come apart.
            AllowAnonymous = targetSymbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() is Endpoints.EndpointAttributeNames.AllowAnonymous
                    or Endpoints.EndpointAttributeNames.MicrosoftAllowAnonymous),
            ExplicitPermission = Actions.Transforms.ActionTransform.ParseExplicitPermission(targetSymbol, context.SemanticModel.Compilation),
            GeneratesPaging = generatesPaging,
            RedundantPagingRequest = pagingIsRequested && declaresPaging,
            // Both halves of the declaration, resolved later: a permission is often a constant this
            // same generator writes, and the catalog that binds it is built after this transform runs.
            RequiredPermissions = declaredPermissions,
            UnresolvedPermissionPaths = unresolvedPermissions,
            RequiresAllPermissions = requiresAll,
            HasBaseQuery = baseQuery is not null,
            BaseQueryTypeName = baseQuery?.ToDisplayString(),
            EagerLoadPaths = eagerLoadPaths,
            EagerLoadProblems = eagerLoadProblems.Select(p => new EagerLoadProblemModel(p.Path, p.Segment)).ToImmutableArray(),
            LoadingProfileFullTypeName = LoadWithProfileReader.ProfileFor(targetSymbol),
            Location = LocationInfo.From(targetSymbol.Locations.FirstOrDefault()),
            InertProcessorCount = targetSymbol.GetAttributes().Count(a =>
                a.AttributeClass?.Name is "PreProcessorAttribute" or "PostProcessorAttribute"),
            ResultTypeGeneratesAProjection = ReturnsDtoParser.GeneratesAProjection(resultType),
            // Only when the DTO maps from an entity: RequiredNavigations is emitted on a [MapFrom]
            // type, and naming a member that will not exist breaks a file the author cannot edit.
            ResponseDtoFullTypeName = ReturnsDtoParser.Read(targetSymbol) is { } responseDto
                                      && ReturnsDtoParser.MapsFromAnEntity(responseDto)
                ? responseDto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null,
        };
    }

    /// <summary>
    ///     Whether the entity's <c>Id</c> is the alias the traits generator writes —
    ///     <c>Id => PersistenceId</c>, unmapped — rather than a column the author declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In the entity's own compilation the alias is not visible here (a generator does not see
    ///         its own output), so the answer is "an <c>[Entity]</c> in the chain, and nobody declared
    ///         <c>Id</c> or <c>PersistenceId</c> by hand". From a referenced assembly the alias is
    ///         visible as metadata, and it is the get-only one: a mapped column has a setter.
    ///     </para>
    ///     <para>
    ///         A hand-declared key (<c>HasManualPersistenceId</c>) may make <c>Id</c> a real column, and
    ///         then a filter on it must stay on it.
    ///     </para>
    /// </remarks>
    internal static bool IdIsTheGeneratedAlias(INamedTypeSymbol entity)
    {
        var isEntity = false;
        for (var type = entity; type is not null; type = type.BaseType)
        {
            if (type.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == EntityTransform.EntityAttributeNonGenericName))
                isEntity = true;

            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol { Name: "Id" or "PersistenceId" } key)
                    continue;
                if (key.DeclaringSyntaxReferences.Length > 0)
                    return false;
                if (key.Name == "Id")
                    return key.SetMethod is null;
            }
        }

        return isEntity;
    }

    private static ImmutableArray<QueryPropertyModel> ExtractProperties(
        INamedTypeSymbol symbol,
        bool idIsTheGeneratedAlias,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<QueryPropertyModel>();

        foreach (var member in symbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IPropertySymbol property)
                continue;

            // Skip indexers and static properties
            if (property.IsIndexer || property.IsStatic)
                continue;

            // Skip properties without getter
            if (property.GetMethod is null)
                continue;

            var model = CreatePropertyModel(property);
            if (model is null)
                continue;

            // The entity's Id is an unmapped alias of PersistenceId: a Where or an OrderBy on it
            // compiles and fails to translate on the first call. The mutation invoker, the repository,
            // the specification and the [Resource] read all target the key themselves; here the
            // transform does it for a hand-written query, so the author does not have to. An explicit
            // MapTo still wins.
            if (idIsTheGeneratedAlias
                && model.MapTo is null
                && (model.IsFilter || model.IsSort)
                && model.EffectivePropertyPath == "Id")
                model = model with { MapTo = "PersistenceId" };

            builder.Add(model);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The <c>[Join&lt;T&gt;]</c> declarations of a query, with <c>Via</c> resolved against the entity.
    /// </summary>
    /// <remarks>
    ///     Through <c>IncludePaths.FirstSegmentThatIsNotANavigation</c> — the reader <c>[EagerLoad]</c>
    ///     and <c>[LoadEntity(Include)]</c> already use — because a navigation is declared or generated
    ///     from a <c>[Relation]</c>, and asking the symbol alone reports every generated one as unknown.
    /// </remarks>
    private static ImmutableArray<(JoinModel Model, ITypeSymbol Target)> ExtractJoins(
        INamedTypeSymbol symbol,
        ITypeSymbol entityType,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<(JoinModel, ITypeSymbol)>();

        foreach (var attr in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            // Check if this is a JoinAttribute<T>
            var attrClassName = attr.AttributeClass?.Name;
            if (attrClassName != "JoinAttribute")
                continue;

            // Verify it's from Pragmatic.Persistence.Query.Attributes namespace
            var attrNamespace = attr.AttributeClass?.ContainingNamespace?.ToDisplayString();
            if (attrNamespace != "Pragmatic.Persistence.Query.Attributes")
                continue;

            // Get the target type from the generic argument
            if (attr.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
                continue;

            var targetType = attrType.TypeArguments[0];

            // Extract named arguments
            var via = GetStringNamedArg(attr, "Via");
            var foreignKey = GetStringNamedArg(attr, "ForeignKey");
            var targetKey = GetStringNamedArg(attr, "TargetKey") ?? "Id";
            var alias = GetStringNamedArg(attr, "Alias");

            // Extract JoinType enum value (default is Inner = 0)
            var joinTypeValue = GetEnumNamedArg(attr, "Type");

            // The two key names are resolved where the author can see them, the same rule Via follows.
            // A key that names nothing would otherwise be a CS1061 inside the generated
            // step, at a line of a file they never opened.
            var foreignKeyType = foreignKey is null ? null : KeyJoinProjection.KeyTypeOn(entityType, foreignKey);
            var targetKeyType = foreignKey is null ? null : KeyJoinProjection.KeyTypeOn(targetType, targetKey);

            builder.Add((new JoinModel
            {
                TargetTypeFullName = targetType.ToDisplayString(),
                TargetTypeName = targetType.Name,
                Via = via,
                UnresolvedSegment = string.IsNullOrWhiteSpace(via)
                    ? null
                    : Actions.Transforms.IncludePaths.FirstSegmentThatIsNotANavigation(entityType, via!),
                ForeignKey = foreignKey,
                TargetKey = targetKey,
                UnresolvedForeignKey = foreignKey is not null && foreignKeyType is null ? foreignKey : null,
                UnresolvedTargetKey = foreignKey is not null && targetKeyType is null ? targetKey : null,
                ForeignKeyMember = foreignKey is null ? null : KeyJoinProjection.EffectiveName(entityType, foreignKey),
                TargetKeyMember = foreignKey is null ? null : KeyJoinProjection.EffectiveName(targetType, targetKey),
                TargetIsReachable = foreignKey is null
                                    || KeyJoinProjection.TargetIsReachableFrom(entityType, targetType),
                ForeignKeyTypeFullName = foreignKeyType,
                TargetKeyTypeFullName = targetKeyType,
                JoinType = (JoinTypeKind)joinTypeValue,
                Alias = alias,
                // A collection navigation is inferred by checking if Via contains "."
                // or if the navigation property points to a collection - but we simplify here
                IsCollection = false
            }, targetType));
        }

        return builder.ToImmutable();
    }

    private static string? GetStringNamedArg(AttributeData attr, string name)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
        return arg.Value.Value?.ToString();
    }

    private static int GetEnumNamedArg(AttributeData attr, string name, int defaultValue = 0)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
        if (arg.Value.Value is int value)
            return value;
        return defaultValue;
    }

    /// <summary>
    ///     Reads one property of a query type — <c>[Filter]</c>, <c>[Sort]</c>, paging by convention —
    ///     into the model the predicate is generated from.
    /// </summary>
    /// <remarks>
    ///     Internal rather than private because <c>[Resource]</c> reads the same thing: a developer
    ///     replacing a scaffolded search's filters declares them as ordinary properties on their part of
    ///     the class, and those have to mean exactly what they mean on a query written by hand. A second
    ///     parser would be a second set of defaults to keep in step — the Contains-for-strings rule
    ///     below is the kind of thing that would drift first.
    /// </remarks>
    internal static QueryPropertyModel? CreatePropertyModel(IPropertySymbol property)
    {
        // Three ways of saying the same thing: this property is not a filter, and something else applies
        // it. None of them names a column, so building a filter from any of them would compare the
        // contribution itself against something.
        //
        //   [ComplexFilter]      — a FilterDto, applied by its generated ApplyFilter
        //   Specification<T>     — applied whole, in Apply and in ToSpecification
        //   GridFilterRequest    — what a grid asks for, applied by the entity's generated bridge
        //   [BindSpecification]  — an input the specification reads; a nullable one would otherwise
        //                          generate a Where *and* feed the rule, applying it twice
        //   [FromClock]          — not an input at all: the invoker writes it, and a specification
        //                          reads it (ClockBindings)
        if (HasAttribute(property, ComplexFilterAttributeName)
            || IsSpecificationType(property.Type)
            || IsGridFilterRequestType(property.Type)
            || HasAttribute(property, BindSpecificationAttributeName)
            || InvokerBinding.IsFromTheClock(property))
            return null;

        var propertyName = property.Name;
        var propertyType = property.Type.ToDisplayString();
        var isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                         property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        // A value bound from the caller is a filter, always applied (IsAlwaysApplied) and matched exactly
        // (below): Contains on an id would read the rows of every caller whose id contains this one.
        var boundFromTheCaller = FromCurrentUserReader.IsBound(property);
        var isRequired = property.IsRequired;

        // Check for [Filter] attribute
        var filterAttr = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FilterAttributeName);

        // Check for [Sort] attribute
        var sortAttr = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SortAttributeName);

        // Determine if this is a paging property by convention
        var isPageProperty = propertyName.Equals("Page", StringComparison.OrdinalIgnoreCase);
        var isPageSizeProperty = propertyName.Equals("PageSize", StringComparison.OrdinalIgnoreCase);

        // A property is a filter if:
        // 1. It has [Filter] attribute, OR
        // 2. It is required (always applied), OR
        // 3. It is nullable and not a sort/page property (optional filter)
        var hasFilterAttribute = filterAttr is not null;

        // [SearchAcross] on text: a filter over the columns it names, not over one named like the property.
        var searchAcross = SearchAcrossReader.Read(property);
        var isSearch = searchAcross is { Paths.Length: > 0 } && IsStringType(property.Type);

        var isFilter = hasFilterAttribute || isRequired || boundFromTheCaller || isSearch ||
                       (isNullable && sortAttr is null && !isPageProperty && !isPageSizeProperty);

        // Exclude paging properties from filters
        if (isPageProperty || isPageSizeProperty)
            isFilter = false;

        // Collection-of-scalars filter (List<string>, string[], …): bound from repeated query keys
        // (?Type=A&Type=B) and applied with In (query.Type.Contains(e.Type)).
        var isCollectionFilter = IsScalarCollectionType(property.Type);

        // Parse filter operator
        var operatorKind = FilterOperatorKind.Equals;
        var operatorExplicit = false;
        string? mapTo = null;
        var ignoreCase = false;

        if (filterAttr is not null)
        {
            foreach (var arg in filterAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "Operator" when arg.Value.Value is int opValue:
                        operatorKind = (FilterOperatorKind)opValue;
                        operatorExplicit = true;
                        break;
                    case "MapTo" when arg.Value.Value is string mapToValue:
                        mapTo = mapToValue;
                        break;
                    case "IgnoreCase" when arg.Value.Value is bool ignoreCaseValue:
                        ignoreCase = ignoreCaseValue;
                        break;
                }
            }

            // Default to Contains for string types ONLY when the operator was not set explicitly.
            // (The attribute's C# default is Equals; without this flag an explicit Operator=Equals on a
            // string property was indistinguishable from the default and got silently rewritten to Contains,
            // making exact string match impossible server-side via [Query].)
            if (!operatorExplicit && IsStringType(property.Type) && !boundFromTheCaller)
                operatorKind = FilterOperatorKind.Contains;
        }

        // A collection filter defaults to In (collection.Contains(entity)). Equals would emit
        // `collection == scalar` and fail to compile. An explicit Operator still wins.
        if (isCollectionFilter && !operatorExplicit)
            operatorKind = FilterOperatorKind.In;

        // Parse sort attribute
        var isSort = sortAttr is not null;
        SortDirectionKind? defaultSortDirection = null;
        var sortPriority = 0;

        if (sortAttr is not null)
        {
            foreach (var arg in sortAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "DefaultDirection" or "Default" when arg.Value.Value is int sortValue:
                        // A SortDirection? reaches the transform as its underlying int: Ascending 0,
                        // Descending 1. Unset means the argument is absent, so this case never runs —
                        // the guard stays for the legacy "Default" key, which is still an int.
                        if (sortValue >= 0)
                            defaultSortDirection = (SortDirectionKind)sortValue;
                        break;
                    case "MapTo" when arg.Value.Value is string sortMapTo:
                        mapTo = sortMapTo;
                        break;
                    case "Priority" when arg.Value.Value is int priority:
                        sortPriority = priority;
                        break;
                }
            }
        }

        return new QueryPropertyModel
        {
            PropertyName = propertyName,
            PropertyType = propertyType,
            QualifiedPropertyType = property.Type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
            IsRequired = isRequired,
            IsBoundFromTheCaller = boundFromTheCaller,
            IsNullable = isNullable,
            Operator = operatorKind,
            MapTo = mapTo,
            // Lowering only means anything on a string; on anything else the generated ToLower() would
            // not compile, so the flag carries "asked for and applicable", not "asked for".
            IgnoreCase = ignoreCase && IsStringType(property.Type),
            IsFilter = isFilter,
            IsCollection = isCollectionFilter,
            IsSort = isSort,
            DefaultSortDirection = defaultSortDirection,
            SortPriority = sortPriority,
            IsPageProperty = isPageProperty,
            IsPageSizeProperty = isPageSizeProperty,
            // Settable, because a get-only computed property is not an input the caller supplies and
            // was never going to be a filter.
            IsInertInput = !isFilter && !isSort && !isPageProperty && !isPageSizeProperty
                           && property.SetMethod is not null,
            SearchAcrossPaths = isSearch ? searchAcross!.Value.Paths : EquatableArray<string>.Empty,
            SearchIgnoresCase = isSearch && searchAcross!.Value.IgnoreCase,
            HasInertSearchAcross = searchAcross is not null && !isSearch,
            HasInertFilterGroup = property.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == FilterGroupAttributeName),
            Location = LocationInfo.From(property.Locations.FirstOrDefault())
        };
    }

    /// <summary>Whether the property carries the attribute with this fully qualified name.</summary>
    private static bool HasAttribute(IPropertySymbol property, string attributeName)
        => property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName);

    /// <summary>Whether the type is a <c>Specification&lt;T&gt;</c>, nullable annotation aside.</summary>
    private static bool IsSpecificationType(ITypeSymbol type)
    {
        var named = type as INamedTypeSymbol;
        return named?.OriginalDefinition.ToDisplayString().StartsWith(SpecificationTypeName + "<", System.StringComparison.Ordinal) == true;
    }

    /// <summary>The <c>Specification&lt;T&gt;</c> properties this query composes.</summary>
    private static ImmutableArray<QuerySpecificationModel> ExtractSpecifications(
        INamedTypeSymbol symbol,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<QuerySpecificationModel>();

        foreach (var member in symbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            // Static included: a rule that reads no input is static, and skipping it made it vanish
            // from the query without a word.
            if (member is not IPropertySymbol { IsIndexer: false } property)
                continue;
            if (property.GetMethod is null)
                continue;
            if (!IsSpecificationType(property.Type))
                continue;

            builder.Add(new QuerySpecificationModel
            {
                PropertyName = property.Name,
                IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated,
                IsStatic = property.IsStatic
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>Whether the type is the canonical <c>GridFilterRequest</c>, nullable annotation aside.</summary>
    private static bool IsGridFilterRequestType(ITypeSymbol type)
        => type.OriginalDefinition.ToDisplayString() == GridFilterRequestTypeName;

    /// <summary>The canonical grid requests this query takes as input.</summary>
    private static ImmutableArray<QueryGridRequestModel> ExtractGridRequests(
        INamedTypeSymbol symbol,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<QueryGridRequestModel>();

        foreach (var member in symbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IPropertySymbol { IsIndexer: false, IsStatic: false } property)
                continue;
            if (property.GetMethod is null)
                continue;
            if (!IsGridFilterRequestType(property.Type))
                continue;

            builder.Add(new QueryGridRequestModel
            {
                PropertyName = property.Name,
                IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>The inputs that claim a specification reads them.</summary>
    private static ImmutableArray<string> ExtractSpecificationInputs(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;
            if (property.GetAttributes().Any(
                    a => a.AttributeClass?.ToDisplayString() == BindSpecificationAttributeName))
                builder.Add(property.Name);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<QueryComplexFilterModel> ExtractComplexFilters(
        INamedTypeSymbol symbol,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<QueryComplexFilterModel>();

        foreach (var member in symbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IPropertySymbol { IsIndexer: false, IsStatic: false } property)
                continue;

            if (property.GetMethod is null)
                continue;

            var hasComplexFilter = property.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == ComplexFilterAttributeName);

            if (!hasComplexFilter)
                continue;

            if (property.Type is not INamedTypeSymbol propType)
                continue;

            // Nullable reference types include '?' in the display name — strip it so generated extension calls are valid
            var typeFullName = propType.ToDisplayString().TrimEnd('?');

            builder.Add(new QueryComplexFilterModel
            {
                PropertyName = property.Name,
                PropertyTypeFullName = typeFullName,
                PropertyTypeName = propType.Name
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Whether a type is a collection of scalar values (List&lt;T&gt;, T[], IEnumerable&lt;T&gt;, …)
    ///     where T is a primitive/string/enum/Guid/date type. <c>byte[]</c> is a scalar (binary), not a collection.
    /// </summary>
    private static bool IsScalarCollectionType(ITypeSymbol type)
    {
        ITypeSymbol? elementType = type switch
        {
            IArrayTypeSymbol array when array.ElementType.SpecialType != SpecialType.System_Byte
                => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
                when named.OriginalDefinition.ToDisplayString()
                    .StartsWith("System.Collections.Generic.", StringComparison.Ordinal)
                => named.TypeArguments[0],
            _ => null
        };

        return elementType is not null && IsScalarElement(elementType);
    }

    private static bool IsScalarElement(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        if (type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String)
            return true;
        if (type.TypeKind == TypeKind.Enum)
            return true;

        var name = type.ToDisplayString();
        return name is "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
            or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly" or "System.Decimal";
    }

    private static bool IsStringType(ITypeSymbol type)
    {
        // Handle nullable strings (string?)
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return nullable.TypeArguments[0].SpecialType == SpecialType.System_String;

        return type.SpecialType == SpecialType.System_String;
    }

    private static INamedTypeSymbol? GetBaseQueryType(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        if (baseType is null || baseType.SpecialType == SpecialType.System_Object)
            return null;

        // Check if base type has [Query] attribute
        var hasQueryAttr = baseType.GetAttributes()
            .Any(a => a.AttributeClass?.Name.StartsWith("QueryAttribute", StringComparison.Ordinal) == true);

        return hasQueryAttr ? baseType : null;
    }

    private static bool IsPartialType(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.PartialKeyword),
            RecordDeclarationSyntax r => r.Modifiers.Any(SyntaxKind.PartialKeyword),
            _ => false
        };
    }

    private static string GetTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol.IsRecord)
            return "record";
        return "class";
    }

    /// <summary>
    ///     The permissions written on the query, split into what binds now and what does not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Split by <c>ActionTransform.ExtractPermissionStrings</c>, the same reader the action,
    ///         endpoint and specification transforms use. A second reader here would be a second answer
    ///         to "what does this attribute say", and the one that drifted would be the one nobody
    ///         looked at.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>[RequirePermission]</c> means all of them and <c>[RequireAnyPermission]</c> means
    ///         one — the same two shapes an action declares. A query carrying both is read as "all",
    ///         which is the safe reading of a contradiction.
    ///     </para>
    /// </remarks>
    private static (ImmutableArray<string> Resolved, ImmutableArray<string> Unresolved, bool RequiresAll)
        ReadPermissions(INamedTypeSymbol symbol, Compilation compilation)
    {
        var resolved = ImmutableArray.CreateBuilder<string>();
        var unresolved = ImmutableArray.CreateBuilder<string>();
        var requiresAll = true;
        var sawAll = false;

        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            var isAll = name is Endpoints.EndpointAttributeNames.RequirePermission
                             or Endpoints.EndpointAttributeNames.LegacyRequirePermission;
            var isAny = name is Endpoints.EndpointAttributeNames.RequireAnyPermission
                             or Endpoints.EndpointAttributeNames.LegacyRequireAnyPermission;
            if (!isAll && !isAny)
                continue;

            var (bound, paths) = Actions.Transforms.ActionTransform.ExtractPermissionStrings(attribute, compilation);
            resolved.AddRange(bound.Where(static p => !string.IsNullOrEmpty(p)));
            unresolved.AddRange(paths);

            if (isAll)
                sawAll = true;
            else if (!sawAll)
                requiresAll = false;
        }

        return (resolved.ToImmutable(), unresolved.ToImmutable(), requiresAll || sawAll);
    }
}
