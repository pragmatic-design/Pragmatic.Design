using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Compositions.Enrichers;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class MutationTransform
{
    private const string MutationBaseTypeName = "Pragmatic.Actions.Mutation.Mutation";
    private const string MapIgnoreAttributeName = "Pragmatic.Mapping.Attributes.MapIgnoreAttribute";

    public static MutationModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetNode is not TypeDeclarationSyntax typeDecl ||
            typeDecl is not (ClassDeclarationSyntax or RecordDeclarationSyntax))
            return null;

        var symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var location = LocationInfo.From(typeDecl.Identifier.GetLocation());

        var isPartial = typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartial)
            return CreateInvalid(ns, symbol, location, MutationInvalidReason.NotPartial);

        var (isMutation, entityType) = AnalyzeMutationBase(symbol);
        if (!isMutation || entityType is null)
            return CreateInvalid(ns, symbol, location, MutationInvalidReason.NoBaseType);

        var mode = DetectMode(context.Attributes, symbol.Name, entityType.Name);
        if (mode is null)
            return CreateInvalid(ns, symbol, location, MutationInvalidReason.ModeNotDetermined);

        var (returnType, softDeleteExplicit) = ParseReturnTypeAndSoftDelete(context.Attributes);
        var (logicalKeyParts, logicalKeyProblem) = returnType == MutationReturnTypeValue.LogicalKey
            ? ReadLogicalKey(entityType, context.SemanticModel.Compilation)
            : (ImmutableArray<MutationKeyPartModel>.Empty, null);
        var entityIdType = EntityTypeHelpers.GetEntityKeyType(entityType, context.SemanticModel.Compilation);
        var directBelongsTo = ParseBelongsTo(symbol);
        // The entity's own [BelongsTo] is the override and wins; where it names none, the
        // boundary that owns it is the single one of its assembly.
        var belongsToTypeName = directBelongsTo
                                ?? (entityType is INamedTypeSymbol namedEntity
                                    ? Core.BoundaryOwnershipReader.QualifiedBoundaryOf(namedEntity)
                                    : null);
        var (includes, includesThatNameNothing) = IncludePaths.EagerLoads(symbol, entityType);
        var (dependencies, ambiguousDependencies) = DependencyFieldParser.Parse(symbol, belongsToTypeName);
        var (loadEntities, loadEntityDiagnostics) =
            ActionTransform.ParseLoadEntities(symbol, context.SemanticModel.Compilation);
        var (loadFromQueries, loadFromDiagnostics) =
            ActionTransform.ParseLoadFromQueries(symbol, context.SemanticModel.Compilation);
        if (!loadEntities.IsEmpty)
            dependencies = ActionTransform.MergeDependencies(dependencies, loadEntities);
        var (unguardedSteps, compensatedSteps, _) = CommitScopeDetector.Detect(
            symbol, typeDecl, context.SemanticModel, belongsToTypeName);
        var (compensatorTypeName, mismatchedCompensator) = CompensatorParser.Parse(symbol, entityType);
        var idPropertyName = FindIdProperty(symbol);

        // Always generate property mappings (even with ApplyAsync override).
        // The MutationInvoker calls ApplyToEntity() BEFORE ApplyAsync(),
        // so auto-mapped properties are set regardless of the dev's override.
        //
        // Which is why nothing here detects that override: the two are different members invoked in a
        // fixed order, so neither has to stand down for the other. Detecting it would state a rule the
        // code does not follow.
        var mappedProperties = ImmutableArray<MutationPropertyMapModel>.Empty;
        var unmappedProperties = ImmutableArray<UnmappedMutationPropertyModel>.Empty;
        var children = ImmutableArray<MutationChildModel>.Empty;
        var links = ImmutableArray<MutationLinkModel>.Empty;
        var retargeted = ImmutableArray<string>.Empty;
        if (mode.Value != MutationModeValue.Delete && mode.Value != MutationModeValue.Restore)
            (mappedProperties, unmappedProperties, children, links, retargeted) = MatchProperties(symbol, entityType, context.SemanticModel.Compilation);

        var inputProperties = InputPropertyHelpers.ParseInputProperties(symbol, context.SemanticModel.Compilation);

        // The generated Id is an input like any other — the boundary facade builds its convenience
        // overload from this list, and a required member it does not know about is a construction site
        // that will not compile. Prepended, because addressing the row comes before changing it.
        var generatesId = idPropertyName is null
                          && mode.Value != MutationModeValue.Create
                          && entityIdType is not null;
        if (generatesId)
        {
            inputProperties = inputProperties.Insert(0, new ActionPropertyModel
            {
                Name = "Id",
                TypeName = entityIdType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsRequired = true,
            });
        }

        // Detect [HasOwner] on entity
        var isOwnedEntity = entityType.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Entity.HasOwnerAttribute");

        // Contributions (delegated to enrichers)
        var softDelete = SoftDeleteEnricher.Enrich(entityType, mode.Value, softDeleteExplicit);
        var (resilience, blankResiliencePolicy) = ResilienceEnricher.Enrich(symbol);
        var isCreate = mode.Value is MutationModeValue.Create or MutationModeValue.CreateOrUpdate;
        var computedDefaults = ComputedDefaultEnricher.Enrich(entityType, isCreate);
        var presets = PresetEnricher.Enrich(entityType, isCreate);
        var filterOverrides = FilterOverrideParser.Parse(symbol);
        var policyTypeFullName = ActionTransform.ParseRequirePolicy(symbol);
        var (requireAllPerms, requireAnyPerms, unresolvedAll, unresolvedAny) = ActionTransform.ParseRequirePermissions(symbol, context.SemanticModel.Compilation);
        var (hasNoValidation, runSync, runAsync, validateIsDeclared) = ActionTransform.ParseDeclaredValidation(symbol);

        return new MutationModel
        {
            HasNoValidation = hasNoValidation,
            RunSync = runSync,
            RunAsync = runAsync,
            ValidateIsDeclared = validateIsDeclared,
            Namespace = ns,
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            DeclaredSubBoundaryName = SubBoundaryTransform.Declared(symbol),
            SubBoundaryName = SubBoundaryTransform.Usable(SubBoundaryTransform.Declared(symbol)),
            SubBoundaryDescription = SubBoundaryTransform.DeclaredDescription(symbol),
            EntityTypeName = entityType.Name,
            EntityFullTypeName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityIdTypeName = entityIdType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Mode = mode.Value,
            // Same rule as an action: unset means the endpoint decides. Without it a mutation existing
            // only as a composite step would be public for ever.
            IsInternal = ParseDeclaredInternal(context.Attributes) ?? !ActionTransform.HasEndpointAttribute(symbol),
            ReturnType = returnType,
            LogicalKeyParts = logicalKeyParts,
            LogicalKeyProblem = logicalKeyProblem,
            Includes = includes,
            // A mode that loads a row is addressed by an Id, and that is implied by the role rather than
            // a choice. When the author declares none the generator writes it — so IdPropertyName is
            // "Id" either way, and the invoker loads by it instead of by nothing.
            IdPropertyName = idPropertyName ?? (generatesId ? "Id" : null),
            GeneratesIdProperty = generatesId,
            // Only when the DTO maps from an entity: RequiredNavigations is emitted on a [MapFrom]
            // type, and naming a member that will not exist breaks a file the author cannot edit.
            ResponseDtoFullTypeName = ReturnsDtoParser.Read(symbol) is { } responseDto
                                      && ReturnsDtoParser.MapsFromAnEntity(responseDto)
                ? responseDto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null,
            Dependencies = dependencies,
            LoadEntities = loadEntities,
            LoadEntityDiagnostics = loadEntityDiagnostics.AddRange(loadFromDiagnostics).AddRange(
                includesThatNameNothing.Select(bad => new LoadEntityDiagnosticInfo
                {
                    EntityTypeName = entityType.Name,
                    Kind = LoadEntityDiagnosticKind.IncludeNotANavigation,
                    AttributeName = "EagerLoad",
                    IncludePath = bad.Path,
                    IncludeSegment = bad.Segment
                })),
            LoadFromQueries = loadFromQueries,
            Bindings = InvokerBindingsTransform.Extract(symbol, context.SemanticModel),
            LoadedValidation = LoadedValidationReader.Read(symbol),
            ForeignBoundaryFacades = unguardedSteps,
            CompensatedForeignSteps = compensatedSteps,
            WritesOwnStore = true,
            CommitMode = CommitScopeDetector.CommitMode(symbol)
                         ?? CommitScopeDetector.InheritedCommitMode(belongsToTypeName, context.SemanticModel),
            CompensatorTypeName = compensatorTypeName,
            MismatchedCompensator = mismatchedCompensator,
            AcceptsPartialWrites = CommitScopeDetector.AcceptsPartialWrites(symbol),
            AmbiguousDependencies = ambiguousDependencies,
            HasBlankResiliencePolicy = blankResiliencePolicy,
            // The same reader the query side uses, so «declared» means the same thing on both.
            HasInertQueryStrategy =
                Persistence.Transforms.QueryStrategyParser.Read(symbol) is not null,
            BelongsToTypeName = belongsToTypeName,
            // Only where it can be wrong: Create makes the row, so it has nothing to address; and a
            // missing Id is not a problem, because the generator supplies one. What remains is an
            // Id the author declared with a type that cannot address the entity — still theirs, still
            // wrong, and still PRAG0435.
            IdProblem = mode.Value == MutationModeValue.Create || idPropertyName is null
                ? MutationIdProblem.None
                : CheckIdProperty(symbol, idPropertyName, entityIdType),
            MappedProperties = mappedProperties,
            Children = children,
            Links = links,
            RetargetedProperties = retargeted,
            AbsorbsChildPermissions = symbol.GetAttributes().Any(
                a => a.AttributeClass?.Name == "AbsorbsChildPermissionsAttribute"),
            AssemblyDeclaresABoundary = BoundaryOwnershipReader.DeclaresAnyBoundary(symbol.ContainingAssembly),
            HasOwnValidator = Validation.Analysis.ValidatablePredictor.WillHaveSyncValidator(
                symbol, context.SemanticModel.Compilation, ct),
            TargetsAChildAggregate = MutationChildAnalyzer.PartOfParentOf(entityType),
            ChildAggregateIsExclusive = MutationChildAnalyzer.PartOfIsExclusive(entityType),
            StateMachineMappedProperty = FindStateMachineCollision(entityType, mappedProperties),
            UnmappedProperties = unmappedProperties,
            InputProperties = inputProperties,
            RaisedEvents = ParseRaisedEvents(symbol, inputProperties, entityType),
            Invariants = ParseInvariants(entityType),
            // What ParseInvariants dropped although it was asked to be a rule — PRAG0463, reported on
            // the method. Collected here rather than inside ParseInvariants so the models the invoker is
            // generated from cannot change shape.
            UncallableInvariants = UncallableInvariants(entityType),
            HasTemporalConstraints = HasTemporalConstraint(entityType),
            IsOwnedEntity = isOwnedEntity,
            EntityHasParameterlessFactory =
                Core.TraitPropertyResolver.WillHaveParameterlessFactory(entityType),
            // The selection Mapping already makes, reused rather than rewritten: it reads the entity's
            // declared constructors, so nothing is predicted here, and [MapConstructor] means the same
            // thing on this path as on ToEntity.
            ConstructorParameters = Mapping.Analysis.ConstructorAnalyzer
                .ExtractConstructorInfo(entityType, symbol).parameters,
            SoftDelete = softDelete,
            EntityDeclaresSoftDelete = entityType is INamedTypeSymbol softDeleteCandidate
                                       && SoftDeleteEnricher.HasGeneratedSoftDeleteFilter(softDeleteCandidate),
            Resilience = resilience,
            Transition = TransitionReader.ForMutation(
                symbol, entityType, mode.Value == MutationModeValue.Update, context.SemanticModel.Compilation),
            ComputedDefaults = computedDefaults,
            Presets = presets,
            FilterOverrides = filterOverrides,
            PolicyTypeFullName = policyTypeFullName,
            EmptyPermissionAttributes = ActionTransform.ParseEmptyPermissionAttributes(symbol, context.SemanticModel.Compilation),
            RequireAllPermissions = requireAllPerms,
            RequireAnyPermissions = requireAnyPerms,
            UnresolvedRequireAllPaths = unresolvedAll,
            UnresolvedRequireAnyPaths = unresolvedAny,
            ExplicitPermission = ActionTransform.ParseExplicitPermission(symbol, context.SemanticModel.Compilation),
            AllowAnonymous = ActionTransform.ParseAllowAnonymous(symbol),
            LocationInfo = location,
            InvalidReason = MutationInvalidReason.None
        };
    }

    private static (bool IsMutation, INamedTypeSymbol? EntityType) AnalyzeMutationBase(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();
            if (baseName.StartsWith(MutationBaseTypeName, StringComparison.Ordinal) &&
                baseType.TypeArguments.Length > 0)
            {
                var entityType = baseType.TypeArguments[0] as INamedTypeSymbol;
                return (true, entityType);
            }
            baseType = baseType.BaseType;
        }
        return (false, null);
    }

    private static MutationModeValue? DetectMode(
        ImmutableArray<AttributeData> attributes,
        string className,
        string entityName)
    {
        var attr = attributes.FirstOrDefault();
        if (attr is not null)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "Mode", Value.Value: int modeValue } && modeValue != 0)
                    return (MutationModeValue)modeValue;
            }
        }

        if (className.StartsWith("Create", StringComparison.Ordinal))
            return MutationModeValue.Create;
        if (className.StartsWith("Update", StringComparison.Ordinal))
            return MutationModeValue.Update;
        if (className.StartsWith("Delete", StringComparison.Ordinal))
            return MutationModeValue.Delete;
        if (className.StartsWith("Restore", StringComparison.Ordinal))
            return MutationModeValue.Restore;

        return null;
    }


    private static string? ParseBelongsTo(ITypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;
            if (attrClass.OriginalDefinition.Name != "BelongsToAttribute")
                continue;
            if (attrClass.TypeArguments.Length > 0)
                return attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        return null;
    }

    /// <summary>
    ///     The key a <c>ReturnType = LogicalKey</c> mutation returns, or why it has none it can type.
    /// </summary>
    private static (ImmutableArray<MutationKeyPartModel> Parts, string? Problem) ReadLogicalKey(
        ITypeSymbol entityType, Compilation compilation)
    {
        if (entityType is not INamedTypeSymbol entity)
            return (ImmutableArray<MutationKeyPartModel>.Empty, $"'{entityType.Name}' is not an entity");

        var parts = MutationLogicalKeyReader.Read(entity, compilation);
        if (parts.IsEmpty)
            return (parts, $"'{entity.Name}' declares no [LogicKey]");

        var untyped = parts.FirstOrDefault(p => p.TypeFullName.Length == 0);
        return untyped is null
            ? (parts, null)
            : (parts, $"the part '{untyped.Name}' of '{entity.Name}'s [LogicKey] is a key that a relation declared on "
                      + "another entity puts there, and its type is known only to the relation graph");
    }

    private static (MutationReturnTypeValue ReturnType, bool SoftDelete) ParseReturnTypeAndSoftDelete(
        ImmutableArray<AttributeData> attributes)
    {
        var returnType = MutationReturnTypeValue.Entity;
        var softDelete = false;

        var attr = attributes.FirstOrDefault();
        if (attr is not null)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "ReturnType", Value.Value: int rtValue })
                    returnType = (MutationReturnTypeValue)rtValue;
                else if (namedArg is { Key: "SoftDelete", Value.Value: bool sd })
                    softDelete = sd;
            }
        }

        return (returnType, softDelete);
    }

    /// <summary>
    ///     What the author wrote for <c>Internal</c>, or <c>null</c> when they left it to the endpoint.
    /// </summary>
    private static bool? ParseDeclaredInternal(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.Name != "MutationAttribute")
                continue;

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Internal" && namedArg.Value.Value is bool declared)
                    return declared;
            }
        }

        return null;
    }

    private static MutationModel CreateInvalid(string ns, INamedTypeSymbol symbol, LocationInfo? location, MutationInvalidReason reason)
    {
        return new MutationModel
        {
            Namespace = ns,
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            EntityTypeName = "",
            EntityFullTypeName = "",
            Mode = MutationModeValue.Create,
            InvalidReason = reason,
            LocationInfo = location
        };
    }

    /// <summary>
    ///     The auto-mapped property that the entity's state machine governs, or null.
    /// </summary>
    /// <remarks>
    ///     Read from <c>[StateMachine&lt;TState&gt;]</c> on the entity, whose <c>Property</c> argument
    ///     defaults to <c>Status</c> — the same default the persistence transform applies, and the reason
    ///     it is repeated here rather than shared: this generator sees the entity symbol, not the
    ///     persistence model, and the two features are assembled independently.
    /// </remarks>
    private static string? FindStateMachineCollision(
        INamedTypeSymbol entityType, ImmutableArray<MutationPropertyMapModel> mapped)
    {
        if (mapped.IsDefaultOrEmpty)
            return null;

        foreach (var attribute in entityType.GetAttributes())
        {
            var definition = attribute.AttributeClass?.OriginalDefinition;
            if (definition is null || definition.Name != "StateMachineAttribute")
                continue;
            if (definition.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.StateMachine")
                continue;

            var property = "Status";
            foreach (var named in attribute.NamedArguments)
                if (named is { Key: "Property", Value.Value: string declared })
                    property = declared;

            foreach (var map in mapped)
                if (string.Equals(map.EntityPropertyName, property, StringComparison.Ordinal))
                    return map.MutationPropertyName;
        }

        return null;
    }
}
