using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    /// <summary>
    ///     The <c>[LoadFrom&lt;TQuery&gt;]</c> properties of an action or a mutation, each with the query's inputs
    ///     bound by name to the operation's properties.
    /// </summary>
    /// <remarks>
    ///     Whether the property is of the query's answer is not decided here: the answer is the query model's
    ///     (<c>QueryModel.AnswerTypeFullName</c>), and it arrives through the pipeline (<c>LoadFromQueries</c>).
    /// </remarks>
    internal static (ImmutableArray<LoadFromQueryModel> Models, ImmutableArray<LoadEntityDiagnosticInfo> Diagnostics)
        ParseLoadFromQueries(INamedTypeSymbol symbol, Compilation compilation)
    {
        var models = ImmutableArray.CreateBuilder<LoadFromQueryModel>();
        var diagnostics = ImmutableArray.CreateBuilder<LoadEntityDiagnosticInfo>();
        var sources = symbol.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.GetMethod is not null && !InvokerBinding.IsLoadedFromAQuery(p))
            .ToList();

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (LoadedQuery(property) is not { } query)
                continue;

            var inputs = ImmutableArray.CreateBuilder<QueryInputBindingModel>();
            var bound = true;

            foreach (var input in QueryInputs(query, symbol, compilation))
            {
                var source = sources.FirstOrDefault(p => string.Equals(p.Name, input.Name, StringComparison.OrdinalIgnoreCase));
                string? problem = null;

                if (source is null)
                {
                    if (!input.IsRequired)
                        continue;
                    problem = $"the required input '{input.Name}' of '{query.Name}' binds no property of '{symbol.Name}' — declare one of that name";
                }
                else if (!compilation.ClassifyCommonConversion(source.Type, input.Type).IsImplicit)
                {
                    problem = $"the input '{input.Name}' of '{query.Name}' is '{input.Type.ToDisplayString()}', and property '{source.Name}' is '{source.Type.ToDisplayString()}'";
                }

                if (problem is not null)
                {
                    diagnostics.Add(new LoadEntityDiagnosticInfo
                    {
                        EntityTypeName = query.Name,
                        Kind = LoadEntityDiagnosticKind.LoadFromInputUnbound,
                        AttributeName = "LoadFrom",
                        Reason = problem
                    });
                    bound = false;
                    continue;
                }

                inputs.Add(new QueryInputBindingModel(input.Name, source!.Name));
            }

            if (!bound)
                continue;

            models.Add(new LoadFromQueryModel
            {
                PropertyName = property.Name,
                PropertyTypeFullName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                QueryTypeFullName = query.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Inputs = inputs.ToImmutable()
            });
        }

        return (models.ToImmutable(), diagnostics.ToImmutable());
    }

    /// <summary>The query a property's <c>[LoadFrom&lt;TQuery&gt;]</c> names, or null without one.</summary>
    private static INamedTypeSymbol? LoadedQuery(IPropertySymbol property)
        => property.GetAttributes()
            .Where(InvokerBinding.IsLoadFrom)
            .Select(a => a.AttributeClass!.TypeArguments[0])
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

    /// <summary>
    ///     The inputs a caller gives the query: the properties it can set from the operation, less those the
    ///     query's own invoker writes (<c>[FromCurrentUser]</c>, <c>[FromClock]</c>).
    /// </summary>
    private static IEnumerable<IPropertySymbol> QueryInputs(INamedTypeSymbol query, INamedTypeSymbol operation, Compilation compilation)
        => query.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic
                        && p.SetMethod is { } setter
                        && compilation.IsSymbolAccessibleWithin(setter, operation)
                        && !InvokerBinding.IsBound(p));
}
