using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Scans entity classes with <c>[StateMachine&lt;TEnum&gt;]</c> and the
///     corresponding enum values for <c>[TransitionFrom]</c>, <c>[InitialState]</c>,
///     <c>[RaisesEvent&lt;T&gt;]</c> to produce <see cref="StateMachineModel" />.
/// </summary>
internal static class StateMachineTransform
{
    public const string StateMachineAttributeName =
        "Pragmatic.Persistence.StateMachine.StateMachineAttribute`1";

    private const string InitialStateAttributeName =
        "Pragmatic.Persistence.StateMachine.InitialStateAttribute";

    private const string TransitionFromAttributeName =
        "Pragmatic.Persistence.StateMachine.TransitionFromAttribute";

    private const string RaisesEventAttributeName =
        "Pragmatic.Persistence.StateMachine.RaisesEventAttribute`1";

    private const string DomainEventSourceTypeName =
        "Pragmatic.Events.DomainEventSource";

    public static StateMachineModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var typeSymbol = context.TargetSymbol as INamedTypeSymbol;
        if (typeSymbol is null)
            return null;

        // [StateMachine<TEnum>] is non-repeatable (AllowMultiple = false): exactly one per entity.
        var attr = context.Attributes.FirstOrDefault();
        if (attr?.AttributeClass is null || !attr.AttributeClass.IsGenericType)
            return null;

        // Extract TEnum from StateMachineAttribute<TEnum>
        var enumType = attr.AttributeClass.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        if (enumType is null || enumType.TypeKind != TypeKind.Enum)
            return null;

        // Read the Property name (defaults to "Status")
        var propertyName = "Status";
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "Property", Value.Value: string propVal })
                propertyName = propVal;
        }

        // Check if entity has domain event support
        var hasDomainEvents = HasBaseType(typeSymbol, DomainEventSourceTypeName);

        // Entity members for resolving raised-event constructor arguments by name. The PK is SG-generated
        // (not yet on the symbol here), so add it explicitly.
        var members = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            if (!member.IsStatic)
                members[member.Name] = member.Name;

        // And the members other generators will add — trait members and the foreign keys of
        // [Relation.*] — which are not on the symbol during this pass. Matched by source members
        // alone, an event parameter named after a generated key was passed `default`.
        foreach (var generated in Core.TraitPropertyResolver.GetGeneratedProperties(typeSymbol))
            if (!members.ContainsKey(generated.Name))
                members[generated.Name] = generated.Name;
        // PK conventions: the SG-generated Id, plus the common {Entity}Id event-parameter name → Id.
        members["Id"] = "Id";
        members["PersistenceId"] = "Id";
        members[typeSymbol.Name + "Id"] = "Id";

        // Scan enum fields for state machine metadata
        var states = CollectStates(enumType, members, ct);

        // Find initial state
        var initialState = states.FirstOrDefault(s => s.IsInitial);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        // Detect user-defined guard methods: CanEnter{State}() returning bool
        var guardMethods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol method &&
                method.Name.StartsWith("CanEnter", StringComparison.Ordinal) &&
                method.Parameters.Length == 0 &&
                method.ReturnType.SpecialType == SpecialType.System_Boolean)
            {
                var stateName = method.Name.Substring("CanEnter".Length);
                if (states.Any(s => s.Name == stateName))
                    guardMethods.Add(stateName);
            }
        }

        return new StateMachineModel
        {
            EntityTypeName = typeSymbol.Name,
            EntityFullTypeName = typeSymbol.ToDisplayString(),
            Namespace = ns,
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            EnumFullTypeName = enumType.ToDisplayString(),
            PropertyName = propertyName,
            // Read here, where the symbol is: the validator only sees the model, and the template that
            // would otherwise emit against a member nobody declared runs after both.
            PropertyExists = typeSymbol.GetMembers(propertyName).OfType<IPropertySymbol>().Any(),
            HasDomainEvents = hasDomainEvents,
            States = states,
            InitialStateName = initialState?.Name,
            GuardedStates = guardMethods.ToImmutableHashSet(),
            IsValid = true
        };
    }

    private static ImmutableArray<StateMachineStateModel> CollectStates(
        INamedTypeSymbol enumType,
        System.Collections.Generic.IReadOnlyDictionary<string, string> members,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<StateMachineStateModel>();

        foreach (var member in enumType.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IFieldSymbol field || !field.HasConstantValue)
                continue;

            var isInitial = false;
            var transitionFrom = ImmutableArray.CreateBuilder<string>();
            var eventTypes = ImmutableArray.CreateBuilder<string>();
            var raisedEvents = ImmutableArray.CreateBuilder<StateRaisedEvent>();

            foreach (var attr in field.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass is null)
                    continue;

                var attrName = attrClass.IsGenericType
                    ? attrClass.OriginalDefinition.ToDisplayString()
                    : attrClass.ToDisplayString();

                if (attrName == InitialStateAttributeName)
                {
                    isInitial = true;
                }
                else if (attrName == TransitionFromAttributeName)
                {
                    // Constructor arg is the source state (object typed, but it's an enum value)
                    if (attr.ConstructorArguments.Length > 0)
                    {
                        var arg = attr.ConstructorArguments[0];
                        // The enum value name — we need the field name, not the numeric value
                        if (arg.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } argEnumType)
                        {
                            var valueName = FindEnumFieldName(argEnumType, arg.Value);
                            if (valueName is not null)
                                transitionFrom.Add(valueName);
                        }
                    }
                }
                else if (attrClass.Name == "RaisesEventAttribute" &&
                         attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.StateMachine")
                {
                    // Extract TEvent from RaisesEventAttribute<TEvent>, resolving its ctor from entity members.
                    if (attrClass.TypeArguments.Length > 0 && attrClass.TypeArguments[0] is INamedTypeSymbol eventType)
                    {
                        eventTypes.Add(eventType.ToDisplayString());
                        raisedEvents.Add(new StateRaisedEvent
                        {
                            TypeName = eventType.ToDisplayString(),
                            CtorArguments = Actions.Transforms.EventConstructorMatcher
                                .ResolveArguments(eventType, members).Arguments
                        });
                    }
                }
            }

            builder.Add(new StateMachineStateModel
            {
                Name = field.Name,
                IsInitial = isInitial,
                TransitionFromStates = transitionFrom.ToImmutable(),
                EventTypeNames = eventTypes.ToImmutable(),
                RaisedEvents = raisedEvents.ToImmutable()
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Finds the field name for an enum constant value.
    /// </summary>
    private static string? FindEnumFieldName(INamedTypeSymbol enumType, object? value)
    {
        if (value is null)
            return null;

        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field)
            {
                if (Equals(field.ConstantValue, value))
                    return field.Name;
            }
        }

        return null;
    }

    /// <summary>
    ///     Checks if a type inherits from a base type with the given fully qualified name.
    /// </summary>
    private static bool HasBaseType(INamedTypeSymbol type, string baseTypeName)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == baseTypeName)
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
