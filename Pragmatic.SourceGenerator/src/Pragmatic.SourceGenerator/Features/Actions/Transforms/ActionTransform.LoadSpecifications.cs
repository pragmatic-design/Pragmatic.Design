using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    /// <summary>
    ///     The rule a load's <c>Specification</c> names, with its parameters bound by name to the
    ///     operation's properties — or why it cannot be read by.
    /// </summary>
    /// <remarks>
    ///     Every question is asked here rather than left to the compiler: a member that is not a
    ///     specification, a parameter with no property, a property of the wrong type would each be an error
    ///     inside the generated invoker, a file the author cannot open, pointing at nothing they wrote.
    /// </remarks>
    private static (string? Member, bool IsInvocation, ImmutableArray<SpecificationArgumentModel> Arguments, LoadEntityDiagnosticInfo? Problem)
        ReadBySpecification(
            AttributeData attr, ITypeSymbol entityType, INamedTypeSymbol operation, Compilation compilation,
            string specificationName)
    {
        var candidates = LoadSpecification.Candidates(attr, entityType, compilation);
        var contract = compilation.GetTypeByMetadataName("Pragmatic.Specification.ISpecification`1")?.Construct(entityType);
        var rules = candidates
            .Where(c => contract is not null && IsSpecificationOf(TypeOf(c), contract)
                                             && c is not IMethodSymbol { IsGenericMethod: true })
            .ToList();

        if (rules.Count == 0)
            return Fail(LoadEntityDiagnosticKind.SpecificationNotFound, candidates.IsEmpty
                ? $"no static member of that name — name one with nameof(Class.Member), or a member of {entityType.Name}Specifications"
                : $"it is not a Specification<{entityType.Name}>");

        if (rules.FirstOrDefault(r => !compilation.IsSymbolAccessibleWithin(r, operation)) is { } hidden)
            return Fail(LoadEntityDiagnosticKind.SpecificationNotFound,
                $"'{hidden.ContainingType.Name}.{hidden.Name}' is not accessible from '{operation.Name}'");

        var bound = rules.Select(r => (Rule: r, Binding: Bind(r, operation, compilation))).ToList();
        var fitting = bound.Where(b => b.Binding.Problem is null).ToList();

        if (fitting.Count > 1)
            return Fail(LoadEntityDiagnosticKind.SpecificationNotFound,
                $"{fitting.Count} overloads bind the operation's properties — name a member that is not overloaded");
        if (fitting.Count == 0)
            return Fail(LoadEntityDiagnosticKind.SpecificationParameterUnbound, bound[0].Binding.Problem!);

        var chosen = fitting[0];
        var member = chosen.Rule.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + chosen.Rule.Name;
        return (member, chosen.Rule is IMethodSymbol, chosen.Binding.Arguments, null);

        (string?, bool, ImmutableArray<SpecificationArgumentModel>, LoadEntityDiagnosticInfo?) Fail(
            LoadEntityDiagnosticKind kind, string reason)
            => (null, false, ImmutableArray<SpecificationArgumentModel>.Empty, new LoadEntityDiagnosticInfo
            {
                EntityTypeName = entityType.Name,
                Kind = kind,
                SpecificationName = specificationName,
                Reason = reason
            });
    }

    /// <summary>
    ///     Each parameter of <paramref name="rule" /> bound by name, ignoring case, to a property of the
    ///     operation whose value converts to it; an optional parameter with no property keeps its default.
    /// </summary>
    private static (ImmutableArray<SpecificationArgumentModel> Arguments, string? Problem) Bind(
        ISymbol rule, INamedTypeSymbol operation, Compilation compilation)
    {
        if (rule is not IMethodSymbol method)
            return (ImmutableArray<SpecificationArgumentModel>.Empty, null);

        var properties = operation.GetMembers().OfType<IPropertySymbol>().ToList();
        var arguments = ImmutableArray.CreateBuilder<SpecificationArgumentModel>();

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
                return (ImmutableArray<SpecificationArgumentModel>.Empty,
                    $"parameter '{parameter.Name}' of '{method.Name}' is passed by reference, which a load cannot bind");

            var property = properties.FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                if (parameter.HasExplicitDefaultValue)
                    continue;

                return (ImmutableArray<SpecificationArgumentModel>.Empty,
                    $"parameter '{parameter.Name}' of '{method.Name}' binds no property of '{operation.Name}' — declare one of that name, or give the parameter a default");
            }

            if (!compilation.ClassifyCommonConversion(property.Type, parameter.Type).IsImplicit)
                return (ImmutableArray<SpecificationArgumentModel>.Empty,
                    $"parameter '{parameter.Name}' of '{method.Name}' is '{parameter.Type.ToDisplayString()}', and property '{property.Name}' is '{property.Type.ToDisplayString()}'");

            arguments.Add(new SpecificationArgumentModel(parameter.Name, property.Name));
        }

        return (arguments.ToImmutable(), null);
    }

    private static ITypeSymbol? TypeOf(ISymbol member) => member switch
    {
        IMethodSymbol method => method.ReturnType,
        IPropertySymbol property => property.Type,
        IFieldSymbol field => field.Type,
        _ => null
    };

    private static bool IsSpecificationOf(ITypeSymbol? type, INamedTypeSymbol contract)
        => type is not null
           && (SymbolEqualityComparer.Default.Equals(type, contract)
               || type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default));
}
