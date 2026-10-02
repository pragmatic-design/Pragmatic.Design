using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     A call in a computed body that filters a sequence with a specification —
///     <c>Lines.Where(LineSpecifications.Shipped)</c>, <c>Lines.Any(LineSpecifications.AtLeast(amount))</c>
///     — and the form a query needs in its place.
/// </summary>
/// <remarks>
///     <para>
///         The call binds to <c>SpecificationExtensions</c>, whose <c>IEnumerable</c> overloads run the
///         compiled delegate: right for the getter, and a call no query provider can translate. In the
///         generated <c>Expr</c>, which does not import the extensions, it was not even that — the
///         argument met <c>Enumerable.Where</c> and the build stopped, so every computed member wrote
///         the rule inline beside the specification that already said it.
///     </para>
///     <para>
///         The query form hands the provider the specification's expression:
///         <c>Queryable.Where(Queryable.AsQueryable(source), (spec).ToExpression())</c>. EF Core evaluates
///         the <c>ToExpression()</c> call before translating — it reads nothing from the row — and inlines
///         the lambda it returns, composed or parameterized. An argument that does read the row cannot be
///         evaluated first, and EF Core throws at the first query: <see cref="ReadsTheRow" /> finds it,
///         and the generator refuses it (PRAG0735).
///     </para>
///     <para>
///         Recognised by its argument, an <c>ISpecification&lt;T&gt;</c>, and not by the method alone:
///         the source is usually a navigation a relation generates, which does not exist while the
///         generator runs, so the call binds to nothing.
///     </para>
/// </remarks>
internal sealed class SpecificationCall
{
    private const string Extensions = "Pragmatic.Specification.SpecificationExtensions";
    private const string SpecificationInterface = "Pragmatic.Specification.ISpecification<T>";

    private SpecificationCall(string method, ExpressionSyntax source, ExpressionSyntax specification)
    {
        Method = method;
        Source = source;
        Specification = specification;
    }

    /// <summary>The method called — <c>Where</c>, <c>Any</c>, <c>Count</c> — and the one of <c>Queryable</c> written.</summary>
    public string Method { get; }

    /// <summary>The sequence filtered: <c>Lines</c> in <c>Lines.Where(spec)</c>.</summary>
    public ExpressionSyntax Source { get; }

    /// <summary>The specification passed: <c>LineSpecifications.AtLeast(amount)</c>.</summary>
    public ExpressionSyntax Specification { get; }

    /// <summary>The call, if <paramref name="node" /> is one; otherwise null.</summary>
    public static SpecificationCall? Of(InvocationExpressionSyntax node, SemanticModel model)
    {
        if (node.Expression is not MemberAccessExpressionSyntax access)
            return null;

        var method = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
        if (method is not null && !IsExtension(method))
            return null;

        var arguments = node.ArgumentList.Arguments;
        ExpressionSyntax source, specification;
        if (method is { IsStatic: true, ReducedFrom: null } && arguments.Count == 2)
        {
            // SpecificationExtensions.Where(source, spec)
            source = arguments[0].Expression;
            specification = arguments[1].Expression;
        }
        else if (arguments.Count == 1)
        {
            source = access.Expression;
            specification = arguments[0].Expression;
        }
        else
        {
            return null;
        }

        return IsSpecification(model.GetTypeInfo(specification).Type)
            ? new SpecificationCall(access.Name.Identifier.ValueText, source, specification)
            : null;
    }

    /// <summary>The call a query can translate, given its source and specification already rewritten.</summary>
    public static string QueryForm(string method, string source, string specification)
        => $"global::System.Linq.Queryable.{method}(global::System.Linq.Queryable.AsQueryable({source}), "
           + $"({specification}).ToExpression())";

    /// <summary>
    ///     Whether the specification takes a value from the row of <paramref name="entity" />: a member
    ///     of the entity, <c>this</c>, or a lambda parameter or local declared in the body around it.
    ///     A constant, a static member or a filter method's parameter is known before the query.
    /// </summary>
    public bool ReadsTheRow(SemanticModel model, INamedTypeSymbol entity, ISet<string> generatedMembers)
    {
        foreach (var node in Specification.DescendantNodesAndSelf())
        {
            if (node is ThisExpressionSyntax or BaseExpressionSyntax)
                return true;
            if (node is not IdentifierNameSyntax name)
                continue;

            // After a dot a name belongs to whatever is on the left, which is checked on its own.
            if (name.Parent is MemberAccessExpressionSyntax access && ReferenceEquals(access.Name, name))
                continue;
            if (name.Parent is NameColonSyntax or NameEqualsSyntax)
                continue;

            var symbol = model.GetSymbolInfo(name).Symbol;
            var readsTheRow = symbol switch
            {
                IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } }
                    or ILocalSymbol or IRangeVariableSymbol => !IsDeclaredWithin(symbol, Specification),
                IPropertySymbol or IFieldSymbol or IMethodSymbol =>
                    !symbol.IsStatic && ProjectableBody.IsMemberOf(entity, symbol.ContainingType),
                _ => generatedMembers.Contains(name.Identifier.ValueText)
                     && ProjectableBody.BindsToTheGeneratedMember(name, symbol, model)
            };

            if (readsTheRow)
                return true;
        }

        return false;
    }

    private static bool IsExtension(IMethodSymbol method)
    {
        // An extension block's member sits in a type nested in the class that declares the block.
        for (var type = (method.ReducedFrom ?? method).ContainingType; type is not null; type = type.ContainingType)
            if (type.ToDisplayString() == Extensions)
                return true;

        return false;
    }

    private static bool IsSpecification(ITypeSymbol? type)
        => type is not null
           && (type.OriginalDefinition.ToDisplayString() == SpecificationInterface
               || type.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == SpecificationInterface));

    private static bool IsDeclaredWithin(ISymbol symbol, SyntaxNode node)
        => symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == node.SyntaxTree && node.Span.Contains(r.Span));
}
