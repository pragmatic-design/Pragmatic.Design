using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Reads <c>[TransitionsTo&lt;TState&gt;(target)]</c> into what the invoker must do with it.
/// </summary>
/// <remarks>
///     <para>
///         The invoker performs the declared transition, so the declaration has to name an entity the invoker can reach — the
///         mutation's own, or exactly one loaded one on an action — and the body must not transition too.
///     </para>
///     <para>
///         Matched on name and namespace, not on a display string: a generic attribute displays with its
///         type argument.
///     </para>
/// </remarks>
internal static class TransitionReader
{
    internal const string AttributeName = "TransitionsToAttribute";
    internal const string AttributeNamespace = "Pragmatic.Actions.Attributes";
    private const string StateMachineAttributeName = "StateMachineAttribute";
    private const string StateMachineNamespace = "Pragmatic.Persistence.StateMachine";

    /// <summary>The declaration on a mutation, whose entity is <paramref name="entityType" />.</summary>
    public static TransitionModel? ForMutation(
        INamedTypeSymbol symbol, INamedTypeSymbol entityType, bool isUpdate, Compilation compilation)
    {
        if (Read(symbol) is not { } declared)
            return null;

        var model = declared.Model with { EntityTypeName = entityType.Name };

        if (!isUpdate)
            return model with { Problem = TransitionProblem.MutationIsNotAnUpdate };

        if (StatePropertyOf(entityType, declared.Enum) is not { } property)
            return model with { Problem = TransitionProblem.NoEntityWithThatStateMachine };

        model = model with { StatePropertyName = property };
        return WithBodyCheck(model, symbol, declared.Enum, compilation);
    }

    /// <summary>The declaration on a domain action, whose entity is one of <paramref name="loads" />.</summary>
    public static TransitionModel? ForAction(
        INamedTypeSymbol symbol, ImmutableArray<LoadEntityModel> loads, Compilation compilation)
    {
        if (Read(symbol) is not { } declared)
            return null;

        var model = declared.Model;

        if (model.Timing == TransitionTimingValue.AfterBody)
            return model with { Problem = TransitionProblem.AfterBodyOnAnAction };

        // One row, always there: a list has no single state, a [RequireExists] holds no row, and an
        // optional load may hold nothing to move.
        var candidates = new List<(LoadEntityModel Load, INamedTypeSymbol Entity, string Property)>();
        foreach (var load in loads)
        {
            if (load.IsMany || load.ExistsOnly || load.IsOptional)
                continue;

            var metadataName = load.EntityTypeFullName.StartsWith("global::", StringComparison.Ordinal)
                ? load.EntityTypeFullName.Substring("global::".Length)
                : load.EntityTypeFullName;

            if (compilation.GetTypeByMetadataName(metadataName) is not { } entity)
                continue;

            if (StatePropertyOf(entity, declared.Enum) is { } property)
                candidates.Add((load, entity, property));
        }

        if (candidates.Count == 0)
            return model with { Problem = TransitionProblem.NoEntityWithThatStateMachine };

        if (candidates.Count > 1)
            return model with
            {
                Problem = TransitionProblem.AmbiguousEntity,
                ProblemDetail = string.Join(", ", candidates.Select(c => c.Load.FieldName))
            };

        var (chosen, chosenEntity, chosenProperty) = candidates[0];
        model = model with
        {
            EntityFieldName = chosen.FieldName,
            EntityTypeName = chosenEntity.Name,
            StatePropertyName = chosenProperty
        };

        return WithBodyCheck(model, symbol, declared.Enum, compilation);
    }

    /// <summary>Whether the operation declares a transition the invoker performs — so it can answer 409.</summary>
    /// <remarks>Read by the endpoint transform, which documents the responses and sees only the symbol.</remarks>
    public static bool InvokerPerformsTransition(INamedTypeSymbol symbol)
        => Read(symbol) is { Model.Timing: not TransitionTimingValue.ByBody };

    private static (TransitionModel Model, INamedTypeSymbol Enum)? Read(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: AttributeName, TypeArguments.Length: 1 } attributeClass
                || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
                || attributeClass.TypeArguments[0] is not INamedTypeSymbol { TypeKind: TypeKind.Enum } stateEnum
                || attribute.ConstructorArguments.Length != 1)
                continue;

            var target = stateEnum.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue
                                     && Equals(f.ConstantValue, attribute.ConstructorArguments[0].Value));
            if (target is null)
                continue;

            var timing = TransitionTimingValue.BeforeBody;
            var conditional = false;
            foreach (var named in attribute.NamedArguments)
            {
                if (named is { Key: "When", Value.Value: int when })
                    timing = (TransitionTimingValue)when;
                else if (named is { Key: "IsConditional", Value.Value: bool isConditional })
                    conditional = isConditional;
            }

            return (new TransitionModel
            {
                EnumFullTypeName = stateEnum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TargetMember = target.Name,
                Timing = timing,
                IsConditional = conditional,
                AttributeLocation = LocationInfo.From(
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation())
            }, stateEnum);
        }

        return null;
    }

    /// <summary>
    ///     The property <paramref name="entity" />'s <c>[StateMachine&lt;TState&gt;]</c> governs, when
    ///     <c>TState</c> is <paramref name="stateEnum" />.
    /// </summary>
    /// <remarks>Same default as the persistence transform: <c>Property</c> is <c>Status</c> unless named.</remarks>
    private static string? StatePropertyOf(INamedTypeSymbol entity, INamedTypeSymbol stateEnum)
    {
        foreach (var attribute in entity.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } attributeClass
                || attributeClass.OriginalDefinition.Name != StateMachineAttributeName
                || attributeClass.OriginalDefinition.ContainingNamespace?.ToDisplayString() != StateMachineNamespace
                || !SymbolEqualityComparer.Default.Equals(attributeClass.TypeArguments[0], stateEnum))
                continue;

            var property = "Status";
            foreach (var named in attribute.NamedArguments)
                if (named is { Key: "Property", Value.Value: string declared })
                    property = declared;

            return property;
        }

        return null;
    }

    /// <summary>
    ///     <see cref="TransitionProblem.BodyAlsoTransitions" /> when the invoker transitions and the
    ///     operation's own code still calls <c>TransitionTo(target)</c>.
    /// </summary>
    /// <remarks>
    ///     The second call is a transition from the target to itself, which the state machine refuses: a
    ///     409 on every call, certain from the source. Only the same target is reported — a body moving the
    ///     entity somewhere else is not certain to fail — and only a call written in this operation: one
    ///     inside a domain method is invisible here, which is what <c>ByBody</c> is for.
    /// </remarks>
    private static TransitionModel WithBodyCheck(
        TransitionModel model, INamedTypeSymbol symbol, INamedTypeSymbol stateEnum, Compilation compilation)
    {
        if (model.Timing == TransitionTimingValue.ByBody)
            return model;

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);

            foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "TransitionTo" }
                    || invocation.ArgumentList.Arguments.Count != 1)
                    continue;

                if (semanticModel.GetSymbolInfo(invocation.ArgumentList.Arguments[0].Expression).Symbol is IFieldSymbol field
                    && SymbolEqualityComparer.Default.Equals(field.ContainingType, stateEnum)
                    && field.Name == model.TargetMember)
                    return model with { Problem = TransitionProblem.BodyAlsoTransitions };
            }
        }

        return model;
    }
}
