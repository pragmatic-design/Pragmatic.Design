using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>One operator of a chain: its name, its call, its lambda and the argument of the two-parameter form.</summary>
internal sealed class DocStep
{
    public DocStep(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        Invocation = invocation;
        Method = method;
        var arguments = invocation.ArgumentList.Arguments;
        Lambda = arguments.Count > 0 ? arguments[0].Expression as LambdaExpressionSyntax : null;
    }

    public string Name => Method.Name;

    public InvocationExpressionSyntax Invocation { get; }

    public IMethodSymbol Method { get; }

    public LambdaExpressionSyntax? Lambda { get; }

    /// <summary>The overload that takes the lambda's values as an argument (<c>Where&lt;TArg&gt;</c>, <c>CountAsync&lt;TArg&gt;</c>).</summary>
    public bool TakesArg => Method.OriginalDefinition.TypeParameters.Any(t => t.Name == "TArg");

    /// <summary>True when the step stores a value in a slot of the chain: an argument, a Select's delegate, a cursor.</summary>
    public bool UsesSlot => TakesArg || Name is "Select" or "After" or "Matching" or "OrderedBy";

    /// <summary>The slots the step takes: a Join four (the inner chain, the two key selectors, the result selector), another one or none.</summary>
    public int Slots => Name == "Join" ? 4 : UsesSlot ? 1 : 0;
}

/// <summary>A call on a <c>DocQuery&lt;T&gt;</c> with its chain walked back to <c>session.Query&lt;T&gt;()</c>.</summary>
internal sealed class DocCall
{
    public const string QueryType = "Pragmatic.Persistence.Documents.DocQuery<T>";
    public const string SessionType = "Pragmatic.Persistence.Documents.DocSession";
    public static readonly HashSet<string> Operators = ["Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Select", "After", "GroupBy", "SelectMany", "Join", "Matching", "OrderedBy"];
    public static readonly HashSet<string> Terminals = ["ToListAsync", "FirstOrDefaultAsync", "CountAsync", "AnyAsync", "MaxAsync", "MinAsync", "SumAsync", "ExecuteDeleteAsync", "ExecuteUpdateAsync"];

    private DocCall(InvocationExpressionSyntax invocation, SemanticModel model, IMethodSymbol method, INamedTypeSymbol? entity, ITypeSymbol element, List<DocStep> before)
    {
        Invocation = invocation;
        Model = model;
        Method = method;
        Entity = entity;
        Element = element;
        Before = before;
    }

    public InvocationExpressionSyntax Invocation { get; }

    public SemanticModel Model { get; }

    public IMethodSymbol Method { get; }

    /// <summary>The [Document] the chain starts at; null when the chain could not be followed back (PRAG3105).</summary>
    public INamedTypeSymbol? Entity { get; }

    /// <summary>The element type of the receiver: the entity, or a projection after a Select.</summary>
    public ITypeSymbol Element { get; }

    /// <summary>The operators from <c>Query&lt;T&gt;()</c> to this call, excluded (Skip and Take are not among them: they run as they are).</summary>
    public List<DocStep> Before { get; }

    public string Name => Method.Name;

    public bool IsTerminal => Terminals.Contains(Name);

    /// <summary>The slot this call's own value takes: after the slots of the steps before it.</summary>
    public int NextSlot => Before.Sum(s => s.Slots);

    public static DocCall? TryCreate(GeneratorSyntaxContext context, CancellationToken ct) => TryCreate((InvocationExpressionSyntax)context.Node, context.SemanticModel, ct);

    /// <summary>The call at <paramref name="invocation" /> as <paramref name="model" /> binds it (the shadow compilation's, for Pragmatic's entities).</summary>
    public static DocCall? TryCreate(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken ct)
    {
        var name = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;
        if (!Operators.Contains(name) && !Terminals.Contains(name))
            return null;
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method || method.ContainingType.OriginalDefinition.ToDisplayString() != QueryType)
            return null;
        var receiver = ((MemberAccessExpressionSyntax)invocation.Expression).Expression;
        var element = (model.GetTypeInfo(receiver, ct).Type as INamedTypeSymbol)?.TypeArguments.FirstOrDefault() ?? method.ContainingType.TypeArguments[0];
        var before = new List<DocStep>();
        var entity = Walk(receiver, model, ct, before, 0);
        return new DocCall(invocation, model, method, entity, element, entity is null ? [] : before);
    }

    /// <summary>A chain written as an argument (a Join's inner chain): its steps and its entity, or null.</summary>
    public static INamedTypeSymbol? StepsOf(ExpressionSyntax chain, SemanticModel model, List<DocStep> steps) => Walk(chain, model, default, steps, 0);

    /// <summary>Back to <c>session.Query&lt;T&gt;()</c> through parentheses, local variables and the DocQuery operators; the entity, or null.</summary>
    private static INamedTypeSymbol? Walk(ExpressionSyntax receiver, SemanticModel model, CancellationToken ct, List<DocStep> steps, int depth)
    {
        if (depth > 64)
            return null;
        switch (receiver)
        {
            case ParenthesizedExpressionSyntax paren:
                return Walk(paren.Expression, model, ct, steps, depth + 1);
            case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier, ct).Symbol is ILocalSymbol local:
                foreach (var reference in local.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                        return Walk(initializer, model, ct, steps, depth + 1);
                }

                return null;
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation when model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method:
            {
                var container = method.ContainingType.OriginalDefinition.ToDisplayString();
                if (container == SessionType && method.Name == "Query" && method.TypeArguments.Length == 1)
                    return method.TypeArguments[0] as INamedTypeSymbol;
                if (container != QueryType)
                    return null;
                var entity = Walk(access.Expression, model, ct, steps, depth + 1);
                if (entity is not null && Operators.Contains(method.Name))
                    steps.Add(new DocStep(invocation, method));
                return entity;
            }

            default:
                return null;
        }
    }
}
