using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>A call site of a SqlQuery operator or terminal, with the chain walked back to its root.</summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>A candidate call site: the invocation, its model, the entity its chain is rooted in, and the operators before it.</summary>
    internal sealed class CallSite
    {
        public CallSite(InvocationExpressionSyntax invocation, SemanticModel model, IMethodSymbol method, INamedTypeSymbol entity, ITypeSymbol element, List<Step> before, bool foreign)
        {
            Invocation = invocation;
            Model = model;
            Method = method;
            Entity = entity;
            Element = element;
            Before = before;
            Foreign = foreign;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public SemanticModel Model { get; }

        public IMethodSymbol Method { get; }

        /// <summary>The [Entity] the chain is rooted in.</summary>
        public INamedTypeSymbol Entity { get; }

        /// <summary>The element type of the receiver (the entity, or a projection after a Select).</summary>
        public ITypeSymbol Element { get; }

        /// <summary>The operators from <c>Query&lt;T&gt;()</c> to this call, excluded.</summary>
        public List<Step> Before { get; }

        /// <summary>True when the method belongs to another provider (System.Linq, EF Core) although the chain is rooted in a SqlQuery: reported, never translated.</summary>
        public bool Foreign { get; }

        public string Name => Method.Name;

        /// <summary>The model the binding reads, whichever compilation it comes from.</summary>
        private readonly struct BoundContext(SemanticModel model)
        {
            public SemanticModel SemanticModel => model;
        }

        public static CallSite? TryCreate(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
            => TryCreate((InvocationExpressionSyntax)ctx.Node, ctx.SemanticModel, ct);

        /// <summary>The call site bound with <paramref name="model" />: the incremental one, or the compilation with Pragmatic's entity shape (<see cref="PragmaticShape" />).</summary>
        public static CallSite? TryCreate(InvocationExpressionSyntax invocation, SemanticModel model, System.Threading.CancellationToken ct)
        {
            var ctx = new BoundContext(model);
            var name = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;
            // A declared chain is called as `Chains.Name(chain, args)`: a static call with a chain first, bound to know (its name is the developer's).
            var fragmentCandidate = invocation.ArgumentList.Arguments.Count >= 1 && ((MemberAccessExpressionSyntax)invocation.Expression).Expression is IdentifierNameSyntax;
            if (!Operators.Contains(name) && !Terminals.Contains(name) && !ForeignCandidate(name) && !fragmentCandidate)
                return null;
            if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
                return null;
            if (InsideSubquery(invocation, ctx.SemanticModel, ct))
                return null; // a chain written inside another chain's lambda: the enclosing plan renders it as a subquery, nothing of its own is intercepted
            if (IsFragment(method))
            {
                var chainBefore = new List<Step>();
                var chainEntity = Walk(invocation.ArgumentList.Arguments[0].Expression, ctx.SemanticModel, ct, chainBefore, 0);
                var chainElement = ElementOf(method.ReturnType);
                return chainEntity is null || chainElement is null ? null : new CallSite(invocation, ctx.SemanticModel, method, chainEntity, chainElement, chainBefore, foreign: false);
            }

            var container = method.ContainingType.OriginalDefinition.ToDisplayString();
            var ours = container == "Pragmatic.Persistence.Sql.SqlQuery<T>";
            if (!ours && !ForeignContainers.Contains(container))
                return null;
            if (ours && !Operators.Contains(name) && !Terminals.Contains(name))
                return null; // Skip, Take: they run as they are
            var receiver = ((MemberAccessExpressionSyntax)invocation.Expression).Expression;
            var element = ElementOf(ctx.SemanticModel.GetTypeInfo(receiver, ct).Type);
            if (element is null)
                return null;
            var before = new List<Step>();
            var entity = Walk(receiver, ctx.SemanticModel, ct, before, 0);
            return entity is null ? null : new CallSite(invocation, ctx.SemanticModel, method, entity, element, before, foreign: !ours);
        }

        /// <summary>True when the invocation sits inside a lambda that is an argument of a SqlQuery operator or terminal: a subquery of that chain.</summary>
        internal static bool InsideSubquery(InvocationExpressionSyntax invocation, SemanticModel model, System.Threading.CancellationToken ct)
        {
            foreach (var lambda in invocation.Ancestors().OfType<LambdaExpressionSyntax>())
            {
                if (lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outer } }
                    && model.GetSymbolInfo(outer, ct).Symbol is IMethodSymbol outerMethod
                    && (outerMethod.ContainingType.OriginalDefinition.ToDisplayString() == "Pragmatic.Persistence.Sql.SqlQuery<T>" || IsFragment(outerMethod)))
                    return true;
            }

            return false;
        }

        /// <summary>A declared chain called in place: the chain it is called on, then its body's steps (bound in the definition's model), one slot per parameter.</summary>
        private static INamedTypeSymbol? WalkFragment(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model, System.Threading.CancellationToken ct, List<Step> steps, int depth, bool parameterRoot)
        {
            if (invocation.ArgumentList.Arguments.Count == 0 || method.Parameters.Length == 0)
                return null;
            var called = Walk(invocation.ArgumentList.Arguments[0].Expression, model, ct, steps, depth + 1, parameterRoot);
            var body = FragmentSteps(method, model.Compilation, ct);
            if (called is null || body is null || !SymbolEqualityComparer.Default.Equals(called, (method.Parameters[0].Type as INamedTypeSymbol)?.TypeArguments.FirstOrDefault()))
                return null;
            steps.Add(new Step("Fragment", invocation, method) { Inner = body, Model = model });
            return called;
        }

        /// <summary>The steps of a declared chain's body, each bound in the definition's model; null when the body is not one chain on the first parameter.</summary>
        internal static List<Step>? FragmentSteps(IMethodSymbol method, Compilation compilation, System.Threading.CancellationToken ct)
        {
            var syntax = method.DeclaringSyntaxReferences.Select(r => r.GetSyntax(ct)).OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var body = syntax?.ExpressionBody?.Expression ?? (syntax?.Body is { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned } ? returned : null);
            if (syntax is null || body is null)
                return null;
            var definitionModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            var steps = new List<Step>();
            var rooted = Walk(body, definitionModel, ct, steps, 0, parameterRoot: true);
            if (rooted is null)
                return null;
            foreach (var step in steps)
                step.Model ??= definitionModel;
            return steps;
        }

        /// <summary>A cheap syntactic gate before binding: System.Linq and EF Core operator names a chain typed IQueryable&lt;T&gt; could bind to.</summary>
        private static bool ForeignCandidate(string name)
            => name is "Skip" or "Take" or "ToList" or "ToArray" or "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Count" or "Any" or "Distinct" or "GroupBy" or "Include" or "AsNoTracking" or "ToDictionaryAsync" or "Sum" or "Max" or "Min" or "Average" or "All" or "Last" or "LastOrDefault" or "Reverse" or "Join" or "SelectMany" or "Concat" or "Union" or "Except" or "Intersect";

        private static ITypeSymbol? ElementOf(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol named)
                return null;
            if (named.Name == "SqlQuery" && named.TypeArguments.Length == 1)
                return named.TypeArguments[0];
            foreach (var i in named.AllInterfaces.Concat([named]))
            {
                if (i is INamedTypeSymbol { Name: "IQueryable", TypeArguments.Length: 1 } queryable && queryable.ContainingNamespace.ToDisplayString() == "System.Linq")
                    return queryable.TypeArguments[0];
            }

            return null;
        }

        /// <summary>
        ///     Walks a receiver down to <c>SqlSession.Query&lt;T&gt;()</c>, collecting the SqlQuery operators on the way
        ///     (through parentheses and local variables initialized from a chain), and returns the entity. Null when
        ///     the chain does not start there: a queryable of another provider, or one handed over by a method. A
        ///     [Query] definition's chain starts at its <c>SqlQuery&lt;T&gt;</c> parameter instead (<paramref name="parameterRoot" />);
        ///     an interceptor never accepts that root, because the caller's own Where operators would share the slots.
        /// </summary>
        internal static INamedTypeSymbol? Walk(ExpressionSyntax receiver, SemanticModel model, System.Threading.CancellationToken ct, List<Step> steps, int depth, bool parameterRoot = false)
        {
            if (depth > 64)
                return null;
            switch (receiver)
            {
                case ParenthesizedExpressionSyntax paren:
                    return Walk(paren.Expression, model, ct, steps, depth + 1, parameterRoot);

                case IdentifierNameSyntax root when parameterRoot && model.GetSymbolInfo(root, ct).Symbol is IParameterSymbol { Type: INamedTypeSymbol { Name: "SqlQuery", TypeArguments.Length: 1 } query }:
                    return query.TypeArguments[0] is INamedTypeSymbol rootedEntity && EntityModel.IsEntity(rootedEntity) ? rootedEntity : null;

                case InvocationExpressionSyntax { Expression: IdentifierNameSyntax } unqualified when model.GetSymbolInfo(unqualified, ct).Symbol is IMethodSymbol fragment && IsFragment(fragment):
                    return WalkFragment(unqualified, fragment, model, ct, steps, depth, parameterRoot); // a declared chain called from its own class

                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation:
                {
                    if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
                        return null;
                    if (IsFragment(method))
                        return WalkFragment(invocation, method, model, ct, steps, depth, parameterRoot);
                    var container = method.ContainingType.OriginalDefinition.ToDisplayString();
                    if (container == "Pragmatic.Persistence.Sql.SqlSession" && method.Name == "Query" && method.TypeArguments.Length == 1)
                        return method.TypeArguments[0] is INamedTypeSymbol rooted && EntityModel.IsEntity(rooted) ? rooted : null;
                    if (container == "Pragmatic.Persistence.Sql.SqlSession" && method.Name == "With" && method.TypeArguments.Length == 1 && invocation.ArgumentList.Arguments.Count >= 1)
                    {
                        // A common table expression as the root: the query (the anchor) walked like a set member, its steps the chain's first slots.
                        if (method.TypeArguments[0] is not INamedTypeSymbol withEntity || !EntityModel.IsEntity(withEntity))
                            return null;
                        var anchor = new List<Step>();
                        var anchorEntity = Walk(invocation.ArgumentList.Arguments[0].Expression, model, ct, anchor, depth + 1, parameterRoot);
                        steps.Add(new Step("With", invocation, method) { Inner = anchorEntity is not null && SymbolEqualityComparer.Default.Equals(anchorEntity, withEntity) ? anchor : null });
                        return withEntity;
                    }

                    if (container == "Pragmatic.Persistence.Sql.SqlSession" && method.Name == "Function" && method.TypeArguments.Length == 1)
                    {
                        // A table-valued function as the root: the first step of the chain, its arguments the first parameters.
                        if (method.TypeArguments[0] is not INamedTypeSymbol functionRoot || !(EntityModel.IsEntity(functionRoot) || EntityModel.IsDeclaredDto(functionRoot)))
                            return null;
                        steps.Add(new Step("Function", invocation, method));
                        return functionRoot;
                    }

                    if (container == "Pragmatic.Persistence.Sql.SqlSession" && method.Name == "Sql" && method.TypeArguments.Length == 1)
                    {
                        // A hand-written statement as the root: the first step of the chain, read by the plan; its rows an entity or a [MapFrom] DTO.
                        if (method.TypeArguments[0] is not INamedTypeSymbol rawRoot || !(EntityModel.IsEntity(rawRoot) || EntityModel.IsDeclaredDto(rawRoot)))
                            return null;
                        steps.Add(new Step("Sql", invocation, method));
                        return rawRoot;
                    }

                    if (container != "Pragmatic.Persistence.Sql.SqlQuery<T>")
                        return null;
                    var entity = Walk(access.Expression, model, ct, steps, depth + 1, parameterRoot);
                    if (entity is null)
                        return null;
                    var step = new Step(method.Name, invocation, method);
                    if ((IsSetOperation(method.Name) || IsJoin(method.Name)) && invocation.ArgumentList.Arguments.Count >= 1)
                    {
                        // The other chain, walked like this one: its steps become a member's (or the joined table's), its slots follow this chain's.
                        var inner = new List<Step>();
                        var innerEntity = Walk(invocation.ArgumentList.Arguments[0].Expression, model, ct, inner, depth + 1, parameterRoot);
                        step.Inner = innerEntity is not null && (IsJoin(method.Name) || SymbolEqualityComparer.Default.Equals(innerEntity, entity)) ? inner : null;
                    }

                    steps.Add(step);
                    // OfType<TDerived>(): the chain is the derived type's from here on (its model, its predicate on the discriminator); the step replays as a check.
                    if (method.Name == "OfType" && method.TypeArguments.Length == 1 && method.TypeArguments[0] is INamedTypeSymbol narrowed && EntityModel.IsEntity(narrowed) && SymbolEqualityComparer.Default.Equals(narrowed.BaseType, entity))
                        return narrowed;
                    return entity;
                }

                case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier, ct).Symbol is ILocalSymbol local:
                {
                    foreach (var reference in local.DeclaringSyntaxReferences)
                    {
                        if (reference.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                            return Walk(initializer, model, ct, steps, depth + 1, parameterRoot);
                    }

                    return null;
                }

                default:
                    return null;
            }
        }
    }
}
