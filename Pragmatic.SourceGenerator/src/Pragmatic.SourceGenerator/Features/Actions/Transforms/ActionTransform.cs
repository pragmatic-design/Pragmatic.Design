using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Compositions.Enrichers;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    public static ActionModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
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

        // Checked before partial-ness: a nested action cannot be generated for at all, and reporting
        // "make it partial" first would send the author to add a keyword that changes nothing.
        if (symbol.ContainingType is not null)
            return new ActionModel
            {
                Namespace = ns,
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsVoid = false,
                InvalidReason = InvalidReason.Nested,
                LocationInfo = LocationInfo.From(classDecl.Identifier.GetLocation())
            };

        var isPartial = classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartial)
            return new ActionModel
            {
                Namespace = ns,
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsVoid = false,
                InvalidReason = InvalidReason.NotPartial,
                LocationInfo = LocationInfo.From(classDecl.Identifier.GetLocation())
            };

        var (isDomainAction, isVoid, returnTypeName, returnTypeSymbol) = AnalyzeDomainAction(symbol);

        if (!isDomainAction)
            return new ActionModel
            {
                Namespace = ns,
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsVoid = false,
                InvalidReason = InvalidReason.NoBaseType,
                LocationInfo = LocationInfo.From(classDecl.Identifier.GetLocation())
            };

        // An error symbol is still an INamedTypeSymbol and its fully-qualified display is the bare name,
        // so everything written about this action would name a type that cannot exist — in files the
        // author cannot edit. Nothing is generated for it; the compiler reports the real
        // error where it is, and PRAG9001 says which operation it stopped.
        if (returnTypeSymbol is { TypeKind: TypeKind.Error })
            return new ActionModel
            {
                Namespace = ns,
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsVoid = false,
                InvalidReason = InvalidReason.UnresolvedResultType,
                UnresolvedResultType = returnTypeSymbol.ToDisplayString(),
                LocationInfo = LocationInfo.From(classDecl.Identifier.GetLocation())
            };

        // Unset means "decide it from the endpoint": one that is routed is surface, one that is not is
        // a step or a helper. A public default would put an operation nobody meant to offer on the
        // interface other modules consume, until someone remembered a flag they did not know existed.
        var (declaredInternal, isSystem) = ParseDomainActionAttribute(context.Attributes);
        var isInternal = declaredInternal ?? !HasEndpointAttribute(symbol);
        var directBelongsTo = ParseDirectBelongsTo(symbol);
        // Third source: the boundary's assembly. Without it, an action that only composes has nothing
        // to deduce from at this stage, and PRAG0432 would ask it for [BelongsTo] — which chooses the
        // boundary and nothing else, so the author would be asked for something the assembly already says.
        var belongsToTypeName = directBelongsTo
                                ?? ParseBelongsToFromEntity(symbol)
                                ?? BoundaryOwnershipReader.SingleBoundaryOf(symbol.ContainingAssembly);
        var (dependencies, ambiguousDependencies) = DependencyFieldParser.Parse(symbol, belongsToTypeName);
        var (unguardedSteps, compensatedSteps, writesOwnStore) = CommitScopeDetector.Detect(
            symbol, classDecl, context.SemanticModel, belongsToTypeName);
        var (compensatorTypeName, mismatchedCompensator) = CompensatorParser.Parse(symbol, returnTypeSymbol);
        var (loadEntities, loadEntityDiagnostics) = ParseLoadEntities(symbol, context.SemanticModel.Compilation);
        var (loadFromQueries, loadFromDiagnostics) = ParseLoadFromQueries(symbol, context.SemanticModel.Compilation);

        if (!loadEntities.IsEmpty)
            dependencies = MergeDependencies(dependencies, loadEntities);

        var inputProperties = InputPropertyHelpers.ParseInputProperties(symbol, context.SemanticModel.Compilation);
        var versions = ParseVersionedExecuteMethods(symbol, isVoid);
        var (resilience, blankResiliencePolicy) = ResilienceEnricher.Enrich(symbol);
        var isComposite = HasCompositeActionAttribute(symbol);
        // Through CompositeActionTransform's own parser rather than a second copy of the rule: the
        // copy that lived here only recognised mutation steps, so a composite made of actions read as
        // "not a composite" everywhere the outer invoker asked.
        var compositeSteps = isComposite
            ? CompositeActionTransform.ParseSteps(symbol)
            : ImmutableArray<CompositeStepModel>.Empty;
        // Only the mutation steps: those are injected as the concrete nested `{mutation}.Invoker` and
        // need registering. An action step is injected by interface, which its own registration
        // already provides.
        var compositeStepInvokers = compositeSteps
            .Where(s => s.Kind == CompositeStepKind.Mutation)
            .Select(s => s.InvokerFullTypeName)
            .ToImmutableArray();
        var filterOverrides = FilterOverrideParser.Parse(symbol);
        var delegationScope = StartsDelegationParser.Parse(symbol, out var badDelegationSubject);
        var (hasNoValidation, runSync, runAsync, syncNestedProps, asyncNestedProps, validateIsDeclared) =
            ParseValidationMetadata(symbol, context.SemanticModel.Compilation);
        var policyTypeFullName = ParseRequirePolicy(symbol);
        var (requireAllPerms, requireAnyPerms, unresolvedAll, unresolvedAny) = ParseRequirePermissions(symbol, context.SemanticModel.Compilation);
        var explicitPermission = ParseExplicitPermission(symbol, context.SemanticModel.Compilation);
        var allowAnonymous = ParseAllowAnonymous(symbol);
        var raisedEvents = ParseRaisedEvents(symbol, inputProperties);

        var declaredSubBoundary = SubBoundaryTransform.Declared(symbol);

        return new ActionModel
        {
            Namespace = ns,
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            DeclaredSubBoundaryName = declaredSubBoundary,
            SubBoundaryName = SubBoundaryTransform.Usable(declaredSubBoundary),
            SubBoundaryDescription = SubBoundaryTransform.DeclaredDescription(symbol),
            IsVoid = isVoid,
            ReturnTypeName = returnTypeName,
            IsInternal = isInternal,
            IsSystem = isSystem,
            IsComposite = isComposite,
            AbsorbsChildPermissions = symbol.GetAttributes().Any(
                a => a.AttributeClass?.Name == "AbsorbsChildPermissionsAttribute"),
            AssemblyDeclaresABoundary = BoundaryOwnershipReader.DeclaresAnyBoundary(symbol.ContainingAssembly),
            HasCompositeSteps = compositeSteps.Length > 0,
            CompositeStepInvokerTypes = compositeStepInvokers,
            BelongsToTypeName = belongsToTypeName,
            BelongsToIsPackage = DirectBelongsToIsPackage(symbol),
            Dependencies = dependencies,
            AmbiguousDependencies = ambiguousDependencies,
            ForeignBoundaryFacades = unguardedSteps,
            CompensatedForeignSteps = compensatedSteps,
            WritesOwnStore = writesOwnStore,
            CommitMode = CommitScopeDetector.CommitMode(symbol)
                         ?? CommitScopeDetector.InheritedCommitMode(belongsToTypeName, context.SemanticModel),
            IsTransactional = CommitScopeDetector.IsTransactional(symbol),
            IsCacheable = symbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Pragmatic.Caching.Attributes.CacheableAttribute"),
            ComposesInvocations = CommitScopeDetector.ComposesInvocations(
                classDecl, context.SemanticModel, belongsToTypeName),
            DeclaresCommitStrategy = CommitScopeDetector.DeclaresCommitStrategy(symbol)
                                     || CommitScopeDetector.InheritedCommitMode(
                                         belongsToTypeName, context.SemanticModel) is not null,
            CompensatorTypeName = compensatorTypeName,
            MismatchedCompensator = mismatchedCompensator,
            AcceptsPartialWrites = CommitScopeDetector.AcceptsPartialWrites(symbol),
            HasBlankResiliencePolicy = blankResiliencePolicy,
            LoadEntities = loadEntities,
            LoadEntityDiagnostics = loadEntityDiagnostics.AddRange(loadFromDiagnostics),
            LoadFromQueries = loadFromQueries,
            Bindings = InvokerBindingsTransform.Extract(symbol, context.SemanticModel),
            LoadedValidation = LoadedValidationReader.Read(symbol),
            InputProperties = inputProperties,
            Versions = versions,
            Resilience = resilience,
            Transition = TransitionReader.ForAction(symbol, loadEntities, context.SemanticModel.Compilation),
            FilterOverrides = filterOverrides,
            DelegationScope = delegationScope,
            BadDelegationSubject = badDelegationSubject?.Property,
            HasNoValidation = hasNoValidation,
            RunSync = runSync,
            RunAsync = runAsync,
            ValidateIsDeclared = validateIsDeclared,
            SyncNestedProperties = syncNestedProps,
            AsyncNestedProperties = asyncNestedProps,
            PolicyTypeFullName = policyTypeFullName,
            EmptyPermissionAttributes = ParseEmptyPermissionAttributes(symbol, context.SemanticModel.Compilation),
            RequireAllPermissions = requireAllPerms,
            RequireAnyPermissions = requireAnyPerms,
            UnresolvedRequireAllPaths = unresolvedAll,
            UnresolvedRequireAnyPaths = unresolvedAny,
            ExplicitPermission = explicitPermission,
            AllowAnonymous = allowAnonymous,
            IsTenantAgnostic = ParseTenantAgnostic(symbol),
            RaisedEvents = raisedEvents,
            InvalidReason = InvalidReason.None,
            LocationInfo = LocationInfo.From(classDecl.Identifier.GetLocation())
        };
    }

    private static (bool IsDomainAction, bool IsVoid, string? ReturnTypeName, ITypeSymbol? ReturnTypeSymbol) AnalyzeDomainAction(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            if (baseName.StartsWith("Pragmatic.Actions.Abstractions.VoidDomainAction"))
                return (true, true, null, null);

            if (baseName.StartsWith("Pragmatic.Actions.Abstractions.DomainAction"))
            {
                if (baseType.TypeArguments.Length > 0)
                {
                    var returnType = baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return (true, false, returnType, baseType.TypeArguments[0]);
                }
                return (true, false, null, null);
            }

            baseType = baseType.BaseType;
        }

        return (false, false, null, null);
    }

    /// <summary>
    ///     Whether the type carries an <c>[Endpoint]</c>, which is what decides visibility when the
    ///     author declared none.
    /// </summary>
    internal static bool HasEndpointAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "EndpointAttribute")
                return true;
        }

        return false;
    }

    private static (bool? Internal, bool IsSystem) ParseDomainActionAttribute(ImmutableArray<AttributeData> attributes)
    {
        bool? isInternal = null;
        var isSystem = false;

        var attr = attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "Pragmatic.Actions.Attributes.DomainActionAttribute");

        if (attr is not null)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Internal" when namedArg.Value.Value is bool internalValue:
                        isInternal = internalValue;
                        break;
                    case "System" when namedArg.Value.Value is bool systemValue:
                        isSystem = systemValue;
                        break;
                }
            }
        }

        return (isInternal, isSystem);
    }

    private static readonly Regex VersionedExecuteRegex = new(@"^Execute(V(\d+)(_(\d+))?)?$", RegexOptions.Compiled);

    private static ImmutableArray<VersionedExecuteModel> ParseVersionedExecuteMethods(INamedTypeSymbol symbol, bool isVoid)
    {
        var builder = ImmutableArray.CreateBuilder<VersionedExecuteModel>();
        var hasBaseExecute = false;
        var hasVersionedExecute = false;

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            var match = VersionedExecuteRegex.Match(member.Name);
            if (!match.Success)
                continue;
            if (member.Parameters.Length != 1)
                continue;
            if (member.Parameters[0].Type.ToDisplayString() != "System.Threading.CancellationToken")
                continue;

            if (match.Groups[1].Success)
            {
                var major = int.Parse(match.Groups[2].Value);
                var minor = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
                builder.Add(new VersionedExecuteModel { Major = major, Minor = minor, MethodName = member.Name });
                hasVersionedExecute = true;
            }
            else
            {
                hasBaseExecute = true;
            }
        }

        if (!hasVersionedExecute)
            return ImmutableArray<VersionedExecuteModel>.Empty;

        if (hasBaseExecute || !isVoid)
        {
            builder.Insert(0, new VersionedExecuteModel { Major = 1, Minor = 0, MethodName = "Execute" });
        }

        builder.Sort((a, b) =>
        {
            var cmp = a.Major.CompareTo(b.Major);
            return cmp != 0 ? cmp : a.Minor.CompareTo(b.Minor);
        });

        return builder.ToImmutable();
    }

    private static bool HasCompositeActionAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == "Pragmatic.Actions.Attributes.CompositeActionAttribute");
    }

}
