using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     Extracts <see cref="SagaModel"/> from [Saga&lt;TState&gt;]-decorated classes.
///     Deduces state graph from [SagaStart], [InState], [CompensateWith&lt;T&gt;] on methods.
/// </summary>
internal static class SagaTransform
{
    public static SagaModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Extract TState from [Saga<TState>]
        var sagaAttr = context.Attributes[0];
        if (sagaAttr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 })
            return null;

        var stateType = sagaAttr.AttributeClass.TypeArguments[0];
        if (stateType.TypeKind != TypeKind.Enum)
            return null;

        var stateTypeFqn = stateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var stateTypeShortName = stateType.Name;

        // Collect enum values
        var stateValues = stateType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f is { IsConst: true, HasConstantValue: true })
            .Select(f => f.Name)
            .ToImmutableArray();

        // Scan methods for saga step attributes
        var steps = ImmutableArray.CreateBuilder<SagaStepModel>();
        SagaStepModel? startStep = null;

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            var isStart = member.GetAttribute(AttributeNames.SagaStart) is not null;

            // Read [InState] attributes
            var inStateAttrs = member.GetAttributes()
                .Where(a => a.AttributeClass?.Name == "InStateAttribute")
                .ToList();

            if (!isStart && inStateAttrs.Count == 0)
                continue;

            var validStates = inStateAttrs
                .Select(a => a.ConstructorArguments.Length > 0
                    ? ResolveEnumMemberName(stateType, a.ConstructorArguments[0].Value)
                    : null)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToImmutableArray();

            var nextState = (string?)null;
            foreach (var attr in inStateAttrs)
            {
                var ns = attr.GetNamedArgument<object>("NextState");
                if (ns is not null)
                {
                    nextState = ResolveEnumMemberName(stateType, ns);
                    break;
                }
            }

            // Read [CompensateWith<T>]
            var compensateAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass is { Name: "CompensateWithAttribute", IsGenericType: true });
            var compensationFqn = compensateAttr?.AttributeClass?.TypeArguments.Length > 0
                ? compensateAttr.AttributeClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;

            // Read [SagaTimeout]
            var timeoutAttr = member.GetAttribute(AttributeNames.SagaTimeout);
            var timeoutDuration = timeoutAttr?.GetNamedArgument<string>("Duration");

            // Extract event type from first parameter
            var eventTypeFqn = (string?)null;
            var eventTypeShortName = (string?)null;
            string? correlationAccessor = null;
            var multipleCorrelationKeys = false;
            if (member.Parameters.Length > 0)
            {
                var paramType = member.Parameters[0].Type;
                eventTypeFqn = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                eventTypeShortName = paramType.Name;
                (correlationAccessor, multipleCorrelationKeys) = ResolveCorrelationAccessor(paramType);
            }

            if (eventTypeFqn is null) continue;

            // Extract return type (the DomainAction to dispatch) + async shape.
            // Task/ValueTask must be awaited by the orchestrator (a sync call would publish the
            // Task object itself and fire-and-forget the step); Task<T>/ValueTask<T> unwrap to T.
            string? returnTypeFqn = null;
            var isAsync = false;
            if (member.ReturnType is INamedTypeSymbol { Name: "Task" or "ValueTask" } awaitable &&
                awaitable.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
            {
                isAsync = true;
                if (awaitable is { IsGenericType: true, TypeArguments.Length: 1 })
                    returnTypeFqn = awaitable.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else if (member.ReturnType.SpecialType != SpecialType.System_Void)
            {
                returnTypeFqn = member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            var step = new SagaStepModel
            {
                MethodName = member.Name,
                EventTypeFqn = eventTypeFqn,
                EventTypeShortName = eventTypeShortName!,
                IsStart = isStart,
                ValidStates = validStates,
                NextState = nextState,
                CompensationActionFqn = compensationFqn,
                TimeoutDuration = timeoutDuration,
                ReturnTypeFqn = returnTypeFqn,
                IsAsync = isAsync,
                CorrelationAccessor = correlationAccessor,
                HasMultipleCorrelationKeys = multipleCorrelationKeys,
            };

            steps.Add(step);
            if (isStart) startStep = step;
        }

        // EF-backed persistence requires TSaga : ISaga<TState>, new(). Detect the interface so the
        // registration template only emits the EF branch for conforming sagas (else it would fail
        // to compile the generated EfCoreSagaRepository<TSaga, TState> line).
        var implementsISaga = symbol.AllInterfaces.Any(i =>
            i.Name == "ISaga"
            && i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging.Saga"
            && i.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], stateType));

        var persistenceBoundary = ResolvePersistenceBoundary(symbol);

        return new SagaModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            AssemblyName = symbol.ContainingAssembly?.Name ?? "",
            IsPartial = symbol.IsPartial(),
            ImplementsISaga = implementsISaga,
            PersistenceDbContextFqn = persistenceBoundary is null ? null : DbContextOf(persistenceBoundary),
            PersistenceBoundaryFqn = persistenceBoundary?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            NamedPersistenceEngine = persistenceBoundary is null ? null : Core.PersistenceEngineMarker.NamedOn(persistenceBoundary),
            BoundaryHasOutbox = persistenceBoundary is not null && Persistence.Sql.Transforms.PragmaticRegistration.EnablesMessagingOutbox(persistenceBoundary),
            HasEnginesSagaStore = context.SemanticModel.Compilation.GetTypeByMetadataName(Persistence.Sql.Transforms.PragmaticRegistration.EngineSagaStore) is not null,
            StateTypeFqn = stateTypeFqn,
            StateTypeShortName = stateTypeShortName,
            Steps = steps.ToImmutable(),
            StartStep = startStep,
            StateValues = stateValues,
            LocationInfo = Pragmatic.SourceGenerator.Core.LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }

    /// <summary>
    ///     The FQN of the generated DbContext of an <c>[EnableSagaPersistence]</c> boundary, by convention
    ///     (<c>{boundaryNamespace}.Entities.{Boundary}DbContext</c>), so a saga on EF Core is registered against an EF Core repository.
    /// </summary>
    private static string DbContextOf(INamedTypeSymbol boundary)
    {
        var boundaryName = boundary.Name.EndsWith("Boundary", System.StringComparison.Ordinal)
            ? boundary.Name.Substring(0, boundary.Name.Length - "Boundary".Length)
            : boundary.Name;
        return $"global::{boundary.ContainingNamespace.ToDisplayString()}.Entities.{boundaryName}DbContext";
    }

    /// <summary>
    ///     Walks up the saga's namespaces to the nearest enclosing <c>[Boundary]</c> type, and returns it when it is
    ///     <c>[EnableSagaPersistence]</c>; otherwise null (in-memory). The boundary lives in the saga's own assembly, so this needs no
    ///     cross-assembly scan.
    /// </summary>
    private static INamedTypeSymbol? ResolvePersistenceBoundary(INamedTypeSymbol saga)
    {
        for (var ns = saga.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                var attrs = type.GetAttributes();
                var isBoundary = attrs.Any(a =>
                    a.AttributeClass is { Name: "BoundaryAttribute" } b
                    && b.ContainingNamespace?.ToDisplayString() == "Pragmatic.Actions.Attributes");
                if (!isBoundary)
                    continue;

                var optedIn = attrs.Any(a =>
                    a.AttributeClass is { Name: "EnableSagaPersistenceAttribute" } s
                    && s.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging.Attributes");
                return optedIn ? type : null;
            }
        }

        return null;
    }

    /// <summary>
    ///     Resolves how the saga correlation id is read off the event:
    ///     <c>ICorrelatedMessage</c> wins (accessor <c>CorrelationId</c>); otherwise the
    ///     <c>[CorrelationKey]</c> property (symbol-based scan — positional record properties
    ///     work, unlike FAWMN). Non-string values are <c>ToString()</c>-ed; nullables coalesce
    ///     to empty so the generated call stays non-nullable. Null accessor = neither present
    ///     (PRAG0820 at generation time).
    /// </summary>
    private static (string? Accessor, bool Multiple) ResolveCorrelationAccessor(ITypeSymbol eventType)
    {
        if (eventType.AllInterfaces.Any(i =>
                i.Name == "ICorrelatedMessage" &&
                i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging.Saga"))
        {
            return ("CorrelationId", false);
        }

        var keys = eventType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.GetAttributes().Any(a =>
                a.AttributeClass is { Name: "CorrelationKeyAttribute" } attrClass &&
                attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging.Attributes"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
            return (null, false);

        var property = keys[0];
        var isString = property.Type.SpecialType == SpecialType.System_String;
        var isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated
            || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        var accessor = (isString, isNullable) switch
        {
            (true, false) => property.Name,
            (true, true) => $"{property.Name} ?? \"\"",
            (false, false) => $"{property.Name}.ToString()",
            (false, true) => $"{property.Name}?.ToString() ?? \"\"",
        };

        return (accessor, keys.Count > 1);
    }

    /// <summary>
    ///     Resolves an enum constant value (int) to its member name.
    ///     [InState(CheckInState.GuestVerified)] passes the int value (1), not the name.
    /// </summary>
    private static string? ResolveEnumMemberName(ITypeSymbol enumType, object? value)
    {
        if (value is null) return null;

        // If it's already a string (e.g., from ToString of a named constant), try to match directly
        var valueStr = value.ToString()!;

        // Check if this is a field name already
        var directMatch = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.Name == valueStr);
        if (directMatch is not null) return directMatch.Name;

        // It's a numeric value — find the matching member
        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member is { IsConst: true, HasConstantValue: true } &&
                member.ConstantValue?.ToString() == valueStr)
            {
                return member.Name;
            }
        }

        return valueStr; // Fallback
    }
}
