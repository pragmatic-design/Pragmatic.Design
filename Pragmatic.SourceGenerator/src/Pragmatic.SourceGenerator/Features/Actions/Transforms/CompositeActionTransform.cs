using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Transforms a [CompositeAction] class into a <see cref="CompositeActionModel"/>
///     by detecting properties whose type derives from Mutation&lt;T&gt;.
/// </summary>
internal static class CompositeActionTransform
{
    private const string MutationBasePrefix = "Pragmatic.Actions.Mutation.Mutation";

    public static CompositeActionModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetNode is not ClassDeclarationSyntax classDecl)
            return null;

        var symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        var isPartial = classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartial)
            return null;

        // Must also be a DomainAction
        var (isDomainAction, isVoid, returnTypeName) = AnalyzeDomainAction(symbol);
        if (!isDomainAction)
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var belongsTo = ParseBelongsTo(symbol);
        var steps = ParseSteps(symbol);

        return new CompositeActionModel
        {
            Namespace = ns,
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            IsVoid = isVoid,
            ReturnTypeName = returnTypeName,
            BelongsToTypeName = belongsTo,
            AbsorbsChildPermissions = symbol.GetAttributes().Any(
                a => a.AttributeClass?.Name == "AbsorbsChildPermissionsAttribute"),
            Steps = steps,
            IsExposed = HasAttribute(symbol, "EndpointAttribute", "Pragmatic.Endpoints.Attributes"),
            DeclaresAuthorization =
                HasAttribute(symbol, "RequirePermissionAttribute", "Pragmatic.Authorization")
                || HasAttribute(symbol, "RequirePolicyAttribute", "Pragmatic.Authorization.Policy")
                || HasAttribute(symbol, "AllowAnonymousAttribute", "Pragmatic.Endpoints.Attributes"),
            StepsRequiringPermission = StepsThatRequireAPermission(symbol),
            IsTransactional = CommitScopeDetector.IsTransactional(symbol),
            DeclaresExecute = symbol.GetMembers("Execute").OfType<IMethodSymbol>().Any(m => !m.IsStatic),
            DeclaresPerStep = CommitScopeDetector.CommitMode(symbol) == "PerStep",
            LocationInfo = LocationInfo.From(classDecl.Identifier.GetLocation())
        };
    }

    /// <summary>
    ///     Reads the step properties: mutations, actions and void actions alike.
    /// </summary>
    /// <remarks>
    ///     Actions are not skipped: a composite made of them would have no steps, get no invoker, and
    ///     run an empty body — the convention for a steps-composite — so it would do nothing, silently.
    ///     Including them works because a step needs no special entry point: the composite claims the
    ///     unit of work before running them, so an ordinary <c>InvokeAsync</c> already stages instead
    ///     of committing.
    /// </remarks>
    internal static ImmutableArray<CompositeStepModel> ParseSteps(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<CompositeStepModel>();

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsImplicitlyDeclared)
                continue;
            if (member.Type is not INamedTypeSymbol propType)
                continue;

            var step = DescribeStep(member.Name, propType);
            if (step is not null)
                builder.Add(step);
        }

        return builder.ToImmutable();
    }

    /// <summary>Matched on name plus namespace: a generic attribute never displays as its bare name.</summary>
    private static bool HasAttribute(INamedTypeSymbol symbol, string name, string containingNamespace)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.OriginalDefinition is not { } declaration)
                continue;
            if (declaration.Name != name)
                continue;
            if (declaration.ContainingNamespace?.ToDisplayString() == containingNamespace)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The step types that declare a permission the composite will suppress.
    /// </summary>
    /// <remarks>
    ///     Read from the step's own type rather than from the model, because what matters is what the
    ///     author of the step decided — the composite has no way to inherit it and does not try.
    /// </remarks>
    private static ImmutableArray<string> StepsThatRequireAPermission(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsImplicitlyDeclared)
                continue;
            if (member.Type is not INamedTypeSymbol stepType)
                continue;
            if (DescribeStep(member.Name, stepType) is null)
                continue;
            if (!HasAttribute(stepType, "RequirePermissionAttribute", "Pragmatic.Authorization")
                && !HasAttribute(stepType, "RequirePolicyAttribute", "Pragmatic.Authorization.Policy"))
                continue;

            // Distinct: two steps of the same type say the same thing twice, and the message reads as
            // though the generator lost count.
            if (!builder.Contains(stepType.Name))
                builder.Add(stepType.Name);
        }

        return builder.ToImmutable();
    }

    private static CompositeStepModel? DescribeStep(string propertyName, INamedTypeSymbol propType)
    {
        var fqn = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var entityType = GetMutationEntityType(propType);
        if (entityType is not null)
        {
            return new CompositeStepModel
            {
                PropertyName = propertyName,
                Kind = CompositeStepKind.Mutation,
                StepFullTypeName = fqn,
                StepTypeName = propType.Name,
                ResultFullTypeName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                // The nested `{mutation}.Invoker`: the composite lives in the same assembly, and the
                // concrete type is what the mutation registration provides.
                InvokerFullTypeName = $"{fqn}.Invoker"
            };
        }

        var (isAction, isVoid, returnType) = DescribeActionBase(propType);
        if (!isAction)
            return null;

        return new CompositeStepModel
        {
            PropertyName = propertyName,
            Kind = isVoid ? CompositeStepKind.VoidAction : CompositeStepKind.Action,
            StepFullTypeName = fqn,
            StepTypeName = propType.Name,
            ResultFullTypeName = returnType,
            // The interface, not the nested Invoker: an action step may live in another assembly of the
            // same boundary, and the registration is by interface anyway.
            InvokerFullTypeName = isVoid
                ? $"global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<{fqn}>"
                : $"global::Pragmatic.Actions.Invoker.IDomainActionInvoker<{fqn}, {returnType}>"
        };
    }

    /// <summary>Whether the property is a domain action, and what it returns.</summary>
    private static (bool IsAction, bool IsVoid, string? ReturnType) DescribeActionBase(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var name = baseType.OriginalDefinition.ToDisplayString();

            if (name.StartsWith("Pragmatic.Actions.Abstractions.VoidDomainAction", StringComparison.Ordinal))
                return (true, true, null);

            if (name.StartsWith("Pragmatic.Actions.Abstractions.DomainAction", StringComparison.Ordinal))
            {
                return baseType.TypeArguments.Length > 0
                    ? (true, false,
                        baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    : (true, true, null);
            }
        }

        return (false, false, null);
    }

    /// <summary>
    ///     Gets the entity type argument from a Mutation&lt;T&gt; base type, or null if not a mutation.
    /// </summary>
    private static INamedTypeSymbol? GetMutationEntityType(INamedTypeSymbol type)
    {
        var current = type;
        while (current is not null)
        {
            var baseName = current.OriginalDefinition.ToDisplayString();
            if (baseName.StartsWith(MutationBasePrefix) && current.TypeArguments.Length > 0)
                return current.TypeArguments[0] as INamedTypeSymbol;

            current = current.BaseType;
        }

        return null;
    }

    private static (bool IsDomainAction, bool IsVoid, string? ReturnTypeName) AnalyzeDomainAction(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            if (baseName.StartsWith("Pragmatic.Actions.Abstractions.VoidDomainAction"))
                return (true, true, null);

            if (baseName.StartsWith("Pragmatic.Actions.Abstractions.DomainAction"))
            {
                if (baseType.TypeArguments.Length > 0)
                {
                    var returnType = baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return (true, false, returnType);
                }
                return (true, false, null);
            }

            baseType = baseType.BaseType;
        }

        return (false, false, null);
    }

    private static string? ParseBelongsTo(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition;
            if (originalDef.Name != "BelongsToAttribute"
                || originalDef.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity")
                continue;

            if (attrClass.TypeArguments.Length > 0)
                return attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }
}
