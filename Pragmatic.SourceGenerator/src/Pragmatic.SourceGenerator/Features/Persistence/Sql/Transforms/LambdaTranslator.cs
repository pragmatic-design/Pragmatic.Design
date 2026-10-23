using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Translates a lambda's body to a SQL fragment at compile time. Columns come from the lambda
///     parameter's members; everything else that is not an operator is a value, which becomes a parameter
///     placeholder <c>{n}</c>. A value is one of three things: a constant (bound from a field of the plan,
///     boxed once); a member of the lambda's argument parameter (<c>(o, a) => o.Since > a.Since</c>: bound
///     from the argument stored in the chain's slot, as the C# text <c>argN.Since</c>); or, where the
///     lambda is not an intercepted call site (a [Query] definition, a visibility rule), the expression as
///     written, read from the definition's scope or from the rule's tree. At a call site a value captured
///     from the enclosing method is reported: no tree carries it, the argument form does. Identifiers are
///     quoted with double quotes; the runtime turns them into brackets for SQL Server.
/// </summary>
internal sealed partial class LambdaTranslator
{
    private SemanticModel _model; // a declared chain's body replayed inside a subquery binds in its own model

    /// <summary>The semantic model the lambda binds in: what a caller asks the symbol of a member it is about to translate.</summary>
    public SemanticModel Model => _model;

    private readonly Func<INamedTypeSymbol, EntityModel?> _entities;
    private LambdaTranslator? _outer; // inside a subquery: the translator of the enclosing lambda, whose row the inner lambdas may read (correlation)
    private RowShape _row; // what the parameter stands for: the entity under its alias, or a join's composite row
    private readonly IParameterSymbol _parameter;
    private Dictionary<IParameterSymbol, string>? _args; // parameters whose values the plan binds from a variable: the two-parameter form's argument, a declared chain's parameters
    private readonly bool _inline;
    private readonly List<string> _parameterPaths; // C# expressions, over the lambda variable `lambda`, that yield the value's Expression node (a rule's tree)
    private readonly List<string> _parameterKinds; // the SqlParamKind name of each, so the plan types its parameters once
    private readonly List<string> _parameterTexts; // the C# text that yields each value: a constant, `argN.Member`, or the expression as written
    private readonly List<bool> _parameterIsConstant;
    private readonly List<string> _joins = [];
    private readonly HashSet<string> _innerAliases = new(StringComparer.Ordinal);
    private readonly GroupKey? _groupKey;
    private string? _error;
    private string? _errorExpression;
    private int _orDepth; // > 0 inside an OR or a NOT: a row may be wanted without its joined row there
    private NullTest _nullTest;
    private ColumnModel? _convertedColumn; // inside a comparison with a converted column: the value on the other side goes through the converter

    /// <summary>The last column a member access translated to: a projection reads a converted column with the column's own reader.</summary>
    public ColumnModel? LastColumn { get; private set; }

    /// <summary>The columns of the root entity the body reads (not a navigation's), for the index analysis of a predicate.</summary>
    public List<ColumnModel> RootColumns { get; } = [];

    /// <summary>The indexed root columns compared with a LIKE that starts with a wildcard (Contains, EndsWith): the index cannot serve it (PRAG3022). The property and the method.</summary>
    public List<(string Property, string Method)> LeadingWildcards { get; } = [];

    /// <summary>
    ///     The pieces of the fragment that differ by dialect (a catalog function's rendering): the fragment holds <c>{dN}</c> where the
    ///     N-th piece goes, and the plan resolves it per dialect. Local numbering; the plan renumbers when it merges.
    /// </summary>
    public List<DialectPiece> DialectPieces { get; }

    /// <summary>The catalog functions whose rendering on a dialect is an emulation: reported at the call site (PRAG3012).</summary>
    public List<(string Function, SqlDialect Dialect, string Note)> Emulations { get; }

    /// <summary>
    ///     True in a projection or an ordering: a compile-time constant is written as a SQL literal instead of a parameter (a select
    ///     list or an ORDER BY does not shape the plan cache, and a literal there costs no parameter). Predicates keep parameters.
    /// </summary>
    public bool InlineConstants { get; set; }

    private enum NullTest
    {
        None,
        IsNull,
        IsNotNull,
    }

    /// <summary>
    ///     With <paramref name="groupKey" /> the parameter is a grouping: <c>g.Key</c> is the key column and <c>g.Count()</c>, <c>g.Sum(x => x.Col)</c>…
    ///     are the aggregates (HAVING, ORDER BY after GroupBy). <paramref name="argParameter" /> is the lambda's second parameter in the
    ///     argument form, whose members are read as <paramref name="argVariable" /> in the plan; <paramref name="inline" /> marks an
    ///     intercepted call site, where a value of the enclosing method cannot be reached and is reported.
    /// </summary>
    public LambdaTranslator(SemanticModel model, Func<INamedTypeSymbol, EntityModel?> entities, EntityModel entity, IParameterSymbol parameter, GroupKey? groupKey = null, IParameterSymbol? argParameter = null, string? argVariable = null, bool inline = false)
        : this(model, entities, new EntityRow(entity, "t"), parameter, groupKey, argParameter, argVariable, inline)
    {
    }

    /// <summary>The same over a row shape: the entity under its alias, or the composite row a join produced.</summary>
    public LambdaTranslator(SemanticModel model, Func<INamedTypeSymbol, EntityModel?> entities, RowShape row, IParameterSymbol parameter, GroupKey? groupKey = null, IParameterSymbol? argParameter = null, string? argVariable = null, bool inline = false)
    {
        _model = model;
        _entities = entities;
        _row = row;
        _parameter = parameter;
        _groupKey = groupKey;
        if (argParameter is not null && argVariable is not null)
            BindParameter(argParameter, argVariable);
        _inline = inline;
        _parameterPaths = [];
        _parameterKinds = [];
        _parameterTexts = [];
        _parameterIsConstant = [];
        DialectPieces = [];
        Emulations = [];
    }

    /// <summary>The values, as paths into the lambda's expression tree (meaningful for a rule, whose tree is built once); the fragment's <c>{n}</c> refers to the n-th.</summary>
    public IReadOnlyList<string> ParameterPaths => _parameterPaths;

    /// <summary>The <c>SqlParamKind</c> of each value, parallel to <see cref="ParameterPaths" />.</summary>
    public IReadOnlyList<string> ParameterKinds => _parameterKinds;

    /// <summary>The C# text that yields each value, parallel to <see cref="ParameterPaths" />: a constant literal, <c>argN.Member</c>, or the expression as written (valid where the definition's names are in scope).</summary>
    public IReadOnlyList<string> ParameterTexts => _parameterTexts;

    /// <summary>True where the value is a compile-time constant, parallel to <see cref="ParameterPaths" />: the plan binds it from a field, boxed once.</summary>
    public IReadOnlyList<bool> ParameterIsConstant => _parameterIsConstant;

    /// <summary>The JOIN fragments the translated members need (navigations).</summary>
    public IReadOnlyList<string> Joins => _joins;

    /// <summary>
    ///     The aliases of the navigations a predicate needs a joined row for: a comparison on the navigation's columns, or an IS NOT
    ///     NULL on it, under ANDs only (not inside an OR, a NOT or an IS NULL). Such a join can be INNER whatever the key's nullability.
    /// </summary>
    public IReadOnlyCollection<string> InnerAliases => _innerAliases;

    public string? Error => _error;

    public string? ErrorExpression => _errorExpression;

    /// <summary>A boolean body as a SQL predicate; null when something was not translatable (see <see cref="Error" />).</summary>
    public string? Predicate(ExpressionSyntax body) => Translate(body, "Captured.Body(lambda)", asPredicate: true);

    /// <summary>A member body as a SQL column (ORDER BY key, scalar Select).</summary>
    public string? ColumnOf(ExpressionSyntax body) => Translate(body, "Captured.Body(lambda)", asPredicate: false);

    private string? Translate(ExpressionSyntax syntax, string path, bool asPredicate)
    {
        syntax = Strip(syntax);
        if (asPredicate && !InlineConstants && syntax is not (LiteralExpressionSyntax or ParenthesizedExpressionSyntax) && _model.GetTypeInfo(syntax).Type?.SpecialType == SpecialType.System_Boolean && IsCapturedValue(syntax) && ReadsNoLambdaParameter(syntax))
        {
            // A boolean of the caller's where a predicate is expected (a permission of the request's context): SQL Server has no boolean
            // expressions, so it is compared with true, as a bare boolean column is.
            var value = Parameter(path, syntax);
            if (value is null)
                return null;
            Add("true", "Boolean", "true", isConstant: true);
            return $"{value} = {{{_parameterPaths.Count - 1}}}";
        }

        switch (syntax)
        {
            case ParenthesizedExpressionSyntax paren:
                return Translate(paren.Expression, path, asPredicate);

            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.IsExpression } or IsPatternExpressionSyntax when TryTypeTest(syntax, negated: false, out var typeTest):
                return typeTest; // x is Derived: the discriminator of a hierarchy's row

            case BinaryExpressionSyntax binary:
                return Binary(binary, path);

            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not when TryTypeTest(not.Operand, negated: true, out var negatedTest):
                return negatedTest; // !(x is Derived): null-safe when the row may be absent

            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not:
            {
                _orDepth++;
                var inner = Translate(not.Operand, $"Captured.Operand({path})", asPredicate: true);
                _orDepth--;
                return inner is null ? null : $"NOT ({inner})";
            }

            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count", Expression: MemberAccessExpressionSyntax array } when IsArrayColumn(array):
                return ArrayLength(array, path); // the elements of an array column

            case MemberAccessExpressionSyntax member when IsOnJsonElement(member):
                return JsonElementMember(member); // inside a JSON array's Any: a member of the element

            case MemberAccessExpressionSyntax member when IsOnJoined(member):
                return JoinedColumn(member, asPredicate);

            case MemberAccessExpressionSyntax member when _outer is not null && ReferenceEquals(_model, _outer._model) && _outer.OwnsMember(member): // a declared chain's body (its own model) never reads the outer row
                ReadsOuter = true;
                return _outer.Translate(member, path, asPredicate); // the enclosing row inside a subquery: its column, under its alias (the joins go to the enclosing statement)

            case MemberAccessExpressionSyntax member when IsOnParameter(member):
            {
                LastColumnOptional = false;
                LastJson = null;
                var column = Column(member);
                // A bare boolean column where a predicate is expected: SQL Server has no boolean expressions, so it is compared to true.
                if (column is not null && asPredicate && _model.GetTypeInfo(member).Type?.SpecialType == SpecialType.System_Boolean)
                {
                    if (InlineConstants)
                    {
                        // Where constants are inlined (a projection's CASE test), true is a literal of the dialect: a projection carries no parameters.
                        DialectPieces.Add(new DialectPiece("1", "true", "1", "true", "true"));
                        return $"{column} = {{d{DialectPieces.Count - 1}}}";
                    }

                    Add("true", "Boolean", "true", isConstant: true);
                    return $"{column} = {{{_parameterPaths.Count - 1}}}";
                }

                return column;
            }

            case MemberAccessExpressionSyntax member when IsCapturedValue(member):
                return Parameter(path, member);

            case IdentifierNameSyntax identifier when IsCapturedValue(identifier):
                return Parameter(path, identifier);

            case LiteralExpressionSyntax literal:
                if (literal.IsKind(SyntaxKind.NullLiteralExpression))
                    return Fail(syntax, "null is only meaningful next to == or !=");
                return Parameter(path, literal); // constants travel as parameters too: no inlining, one plan per shape

            case InvocationExpressionSyntax invocation:
                return Invocation(invocation, path);

            case ConditionalExpressionSyntax conditional:
            {
                // CASE WHEN … THEN … ELSE … END as a value; as a predicate its boolean form (SQL Server has no boolean expressions to branch into).
                if (asPredicate)
                    return ConditionalPredicate(conditional, path);
                var test = Translate(conditional.Condition, $"Captured.Test({path})", asPredicate: true);
                var whenTrue = Translate(conditional.WhenTrue, $"Captured.IfTrue({path})", asPredicate: false);
                var whenFalse = Translate(conditional.WhenFalse, $"Captured.IfFalse({path})", asPredicate: false);
                return test is null || whenTrue is null || whenFalse is null ? null : $"CASE WHEN {test} THEN {whenTrue} ELSE {whenFalse} END";
            }

            default:
                if (IsCapturedValue(syntax))
                    return Parameter(path, syntax);
                return Fail(syntax, $"a {syntax.Kind()} is not in the translated subset");
        }
    }

    /// <summary>Casts, the null-forgiving <c>!</c> and parentheses say nothing to SQL.</summary>
    private static ExpressionSyntax Strip(ExpressionSyntax syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case CastExpressionSyntax cast:
                    syntax = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang:
                    syntax = bang.Operand;
                    continue;
                case ParenthesizedExpressionSyntax paren:
                    syntax = paren.Expression;
                    continue;
                default:
                    return syntax;
            }
        }
    }

    private string? Binary(BinaryExpressionSyntax binary, string path)
    {
        var kind = binary.Kind();
        if (kind is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            var or = kind == SyntaxKind.LogicalOrExpression;
            if (or)
                _orDepth++;
            var l = Translate(binary.Left, $"Captured.Left({path})", asPredicate: true);
            var r = Translate(binary.Right, $"Captured.Right({path})", asPredicate: true);
            if (or)
                _orDepth--;
            return l is null || r is null ? null : $"({l}) {(or ? "OR" : "AND")} ({r})";
        }

        if (kind == SyntaxKind.CoalesceExpression)
        {
            var first = Translate(binary.Left, $"Captured.Left({path})", asPredicate: false);
            var fallback = Translate(binary.Right, $"Captured.Right({path})", asPredicate: false);
            return first is null || fallback is null ? null : $"COALESCE({first}, {fallback})";
        }

        var op = kind switch
        {
            SyntaxKind.EqualsExpression => "=",
            SyntaxKind.NotEqualsExpression => "<>",
            SyntaxKind.LessThanExpression => "<",
            SyntaxKind.LessThanOrEqualExpression => "<=",
            SyntaxKind.GreaterThanExpression => ">",
            SyntaxKind.GreaterThanOrEqualExpression => ">=",
            SyntaxKind.AddExpression => "+",
            SyntaxKind.SubtractExpression => "-",
            SyntaxKind.MultiplyExpression => "*",
            SyntaxKind.DivideExpression => "/",
            _ => null,
        };
        if (op is null)
            return Fail(binary, $"operator {kind} is not translated");

        var leftNull = Strip(binary.Left).IsKind(SyntaxKind.NullLiteralExpression);
        var rightNull = Strip(binary.Right).IsKind(SyntaxKind.NullLiteralExpression);
        if ((leftNull || rightNull) && kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
        {
            _nullTest = kind == SyntaxKind.EqualsExpression ? NullTest.IsNull : NullTest.IsNotNull;
            var operand = Translate(leftNull ? binary.Right : binary.Left, leftNull ? $"Captured.Right({path})" : $"Captured.Left({path})", asPredicate: false);
            _nullTest = NullTest.None;
            return operand is null ? null : $"{operand} IS {(kind == SyntaxKind.EqualsExpression ? "" : "NOT ")}NULL";
        }

        // A converted column on one side: the value on the other goes through the converter and binds as the store kind.
        _convertedColumn = ConvertedColumnOf(binary.Left) ?? ConvertedColumnOf(binary.Right);
        var left = Translate(binary.Left, $"Captured.Left({path})", asPredicate: false);
        var right = Translate(binary.Right, $"Captured.Right({path})", asPredicate: false);
        _convertedColumn = null;
        return left is null || right is null ? null : $"{left} {op} {right}";
    }

    private ColumnModel? ConvertedColumnOf(ExpressionSyntax side)
    {
        if (Strip(side) is not MemberAccessExpressionSyntax member || !(IsOnParameter(member) || IsOnJoined(member)) || Segments(member, out var casts, IsOnJoined(member)) is not { } segments)
            return null;
        // The column the path ends on, through the row's shape (a composite member, an owned type's member) and the navigations on the way (no joins here: the path only decides the converter): converted or not.
        var at = (IsOnJoined(member) ? _joined! : this).EntityAt(segments, member);
        if (at is null || at.Value.Index >= segments.Count)
            return null;
        var entity = at.Value.Entity;
        var i = at.Value.Index;
        for (; i < segments.Count - 1; i++)
        {
            var navigation = Narrowed(entity, casts[i], member, out _)?.Navigation(segments[i]);
            var target = navigation is null ? null : _entities(navigation.Target);
            if (target is null)
                break; // an owned type's member: the rest of the path names it
            entity = target;
        }

        var ending = Narrowed(entity, casts[segments.Count - 1], member, out _);
        var path = string.Join(".", segments.Skip(i));
        return ending?.Column(path) is { Converter: not null } column ? column : null;
    }

    private bool IsOnParameter(MemberAccessExpressionSyntax member)
    {
        var root = Strip(member.Expression);
        while (root is MemberAccessExpressionSyntax inner)
            root = Strip(inner.Expression);
        return root is IdentifierNameSyntax identifier && _model.GetSymbolInfo(identifier).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, _parameter);
    }

    /// <summary>True when nothing in the expression refers to the lambda parameter: it is a value the lambda captured.</summary>
    private bool IsCapturedValue(ExpressionSyntax syntax)
    {
        foreach (var node in syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (_model.GetSymbolInfo(node).Symbol is IParameterSymbol p && OwnsParameter(p))
                return false;
        }

        return true;
    }

    /// <summary>True when no lambda parameter is read (an inner lambda's element is not a value of the caller's), the argument of the two-parameter form aside.</summary>
    private bool ReadsNoLambdaParameter(ExpressionSyntax syntax)
    {
        foreach (var node in syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (_model.GetSymbolInfo(node).Symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } } p && !(_args?.ContainsKey(p) ?? false))
                return false;
        }

        return true;
    }

    /// <summary>True for this lambda's row parameter, its joined row's, or an enclosing lambda's when inside a subquery.</summary>
    private bool OwnsParameter(IParameterSymbol p) => SymbolEqualityComparer.Default.Equals(p, _parameter) || IsJoinedParameter(p) || (_outer?.OwnsParameter(p) ?? false);

    /// <summary>True when the member path is rooted in a row this translator (or an enclosing one) stands for.</summary>
    private bool OwnsMember(MemberAccessExpressionSyntax member) => IsOnParameter(member) || IsOnJoined(member) || (_outer?.OwnsMember(member) ?? false);

    private string? Fail(SyntaxNode node, string why)
    {
        _error ??= why;
        _errorExpression ??= node.ToString();
        return null;
    }
}
