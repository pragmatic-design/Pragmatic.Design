using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A hierarchy in one table inside a lambda: <c>x is Derived</c> is a test on the discriminator of the row <c>x</c> names (the
///     parameter, or a navigation of it); a cast <c>((Derived)x)</c> reads the derived type's own members, its columns and its
///     navigations (a LEFT JOIN: the row may be of another type); and a conditional over such tests where a predicate is expected
///     is the boolean form of the CASE, <c>(test AND a) OR (NOT test AND b)</c>, the joins it reaches left optional. ASSUMPTION: the
///     test on an optional row that is absent is unknown in SQL; negated it is written null-safe, so the else arm takes such a row
///     as EF Core's CASE does.
/// </summary>
internal sealed partial class LambdaTranslator
{
    /// <summary>The [Entity] a cast around the expression names (through parentheses and <c>!</c>); null without one (a cast to a value type says nothing to SQL).</summary>
    private INamedTypeSymbol? CastOf(ExpressionSyntax syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case ParenthesizedExpressionSyntax paren:
                    syntax = paren.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang:
                    syntax = bang.Operand;
                    continue;
                case CastExpressionSyntax cast:
                    return _model.GetTypeInfo(cast.Type).Type is INamedTypeSymbol named && EntityModel.IsEntity(named) ? named : CastOf(cast.Expression);
                default:
                    return null;
            }
        }
    }

    /// <summary>
    ///     The model a cast narrows the entity to: the derived type's own, whose members (its columns, its navigations) the path may then
    ///     read; the entity itself without a cast, or when the cast names the type it is or its root. Null with the error when the cast
    ///     is not to a type below the entity, at any depth.
    /// </summary>
    private EntityModel? Narrowed(EntityModel entity, INamedTypeSymbol? cast, SyntaxNode where, out bool narrowed)
    {
        narrowed = false;
        if (cast is null || SymbolEqualityComparer.Default.Equals(cast, entity.Type) || EntityModel.Below(entity.Type, cast))
            return entity;
        var derived = EntityModel.Below(cast, entity.Type) ? _entities(cast) : null;
        if (derived is null)
        {
            Fail(where, $"a cast reads a derived type of a hierarchy's root: {cast.Name} does not derive from {entity.Type.Name}");
            return null;
        }

        narrowed = true;
        return derived;
    }

    /// <summary>True when the expression is a type test (<c>x is T</c>, <c>x is not T</c>, in a pattern or not), with its translation (null with the error).</summary>
    private bool TryTypeTest(ExpressionSyntax syntax, bool negated, out string? result)
    {
        result = null;
        switch (Strip(syntax))
        {
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.IsExpression } test when test.Right is TypeSyntax type:
                result = TypeTest(test.Left, type, negated, test);
                return true;
            case IsPatternExpressionSyntax { Pattern: TypePatternSyntax pattern } test:
                result = TypeTest(test.Expression, pattern.Type, negated, test);
                return true;
            case IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax { Expression: TypeSyntax constantType } } test when _model.GetSymbolInfo(constantType).Symbol is INamedTypeSymbol:
                result = TypeTest(test.Expression, constantType, negated, test);
                return true;
            case IsPatternExpressionSyntax { Pattern: UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern, Pattern: TypePatternSyntax pattern } } test:
                result = TypeTest(test.Expression, pattern.Type, !negated, test);
                return true;
            case IsPatternExpressionSyntax { Pattern: UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern, Pattern: ConstantPatternSyntax { Expression: TypeSyntax constantType } } } test when _model.GetSymbolInfo(constantType).Symbol is INamedTypeSymbol:
                result = TypeTest(test.Expression, constantType, !negated, test);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///     <c>x is Derived</c> on a row of a hierarchy: the derived type's predicate on the discriminator, under the row's alias (the
    ///     joins to reach it added); negated, null-safe when the row may be absent.
    /// </summary>
    private string? TypeTest(ExpressionSyntax operand, TypeSyntax typeSyntax, bool negated, SyntaxNode where)
    {
        if (_model.GetTypeInfo(typeSyntax).Type is not INamedTypeSymbol type || !EntityModel.IsEntity(type))
            return Fail(where, $"'{typeSyntax}' is not an [Entity]: a type test reads the discriminator of a hierarchy in one table");
        var located = EntityOf(operand, where);
        if (located is null)
            return null;
        var (entity, alias, optional) = located.Value;
        var derived = Narrowed(entity, type, where, out _);
        if (derived is null)
            return null;
        if (derived.DiscriminatorPredicate is not { } discriminatorPredicate)
            return Fail(where, $"{type.Name} is not a derived type of a hierarchy: 'is {type.Name}' has no discriminator to test");
        if (entity.Tpc && !entity.IsHierarchyRoot)
            return Fail(where, $"the rows of {entity.Type.Name} are all of that type (a table per concrete type): 'is {type.Name}' is a test on the root's rows");
        var discriminator = entity.Columns.FirstOrDefault(c => c.IsDiscriminator)?.Column ?? "Discriminator";
        var test = discriminatorPredicate.Replace("t.\"", alias + ".\"");
        if (!negated)
            return test;
        return optional ? $"({alias}.\"{discriminator}\" IS NULL OR NOT ({test}))" : $"NOT ({test})";
    }

    /// <summary>The entity a path ends on (<c>x</c> the parameter, <c>x.Nav</c>, <c>x.Nav.Nav</c>, each hop through its cast): its model, its alias, whether its row may be absent.</summary>
    private (EntityModel Entity, string Alias, bool Optional)? EntityOf(ExpressionSyntax operand, SyntaxNode where)
    {
        var stripped = Strip(operand);
        if (stripped is IdentifierNameSyntax identifier)
        {
            if (!(_model.GetSymbolInfo(identifier).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, _parameter)))
            {
                Fail(where, $"'{identifier}' is not the lambda parameter: a type test reads the row or a navigation of it");
                return null;
            }

            if (_row is not EntityRow row)
            {
                Fail(where, "a type test reads an entity row: name the member of the joined row (x.Order is Derived)");
                return null;
            }

            return (row.Entity, row.Alias, row.Optional);
        }

        if (stripped is not MemberAccessExpressionSyntax member || !IsOnParameter(member) || Segments(member, out var casts) is not { } segments)
        {
            Fail(where, $"'{operand}' is not the lambda parameter or a navigation of it: a type test reads the row's discriminator");
            return null;
        }

        var at = EntityAt(segments, member, out var rendered);
        if (at is null)
        {
            if (rendered is not null)
                Fail(where, $"'{operand}' is a value the join already projected: a type test reads an entity row");
            return null;
        }

        var (entity, alias, start, optional, _) = at.Value;
        for (var i = start; i < segments.Count; i++)
        {
            var model = Narrowed(entity, casts[i], where, out var narrowed);
            var target = model is null ? null : Hop(model, ref alias, ref optional, segments[i], narrowed ? casts[i]!.Name : null, where, wanted: false);
            if (target is null)
                return null;
            entity = target;
        }

        return (entity, alias, optional);
    }

    /// <summary>
    ///     <c>test ? a : b</c> where a predicate is expected: the boolean form of the CASE, <c>(test AND a) OR (NOT test AND b)</c>, the arms
    ///     predicates themselves (a nested conditional recurses); a <c>true</c> or <c>false</c> arm folds (<c>test ? a : true</c> is
    ///     <c>NOT test OR a</c>). The joins the test or an arm reaches stay optional: a row matches through either side.
    /// </summary>
    private string? ConditionalPredicate(ConditionalExpressionSyntax conditional, string path)
    {
        _orDepth++;
        var test = Translate(conditional.Condition, $"Captured.Test({path})", asPredicate: true);
        var negated = TryTypeTest(conditional.Condition, negated: true, out var nullSafe) ? nullSafe : test is null ? null : $"NOT ({test})";
        var whenTrue = Arm(conditional.WhenTrue, $"Captured.IfTrue({path})", out var trueIsTrue, out var trueIsFalse);
        var whenFalse = Arm(conditional.WhenFalse, $"Captured.IfFalse({path})", out var falseIsTrue, out var falseIsFalse);
        _orDepth--;
        if (test is null || negated is null || whenTrue is null || whenFalse is null)
            return null;
        if (trueIsTrue && falseIsTrue)
            return "1 = 1";
        if (trueIsFalse && falseIsFalse)
            return "1 = 0";
        if (trueIsTrue && falseIsFalse)
            return test;
        if (trueIsFalse && falseIsTrue)
            return negated;
        if (trueIsTrue)
            return $"({test}) OR ({whenFalse})";
        if (trueIsFalse)
            return $"({negated}) AND ({whenFalse})";
        if (falseIsTrue)
            return $"({negated}) OR ({whenTrue})";
        if (falseIsFalse)
            return $"({test}) AND ({whenTrue})";
        return $"(({test}) AND ({whenTrue})) OR (({negated}) AND ({whenFalse}))";
    }

    /// <summary>An arm of a conditional predicate: a boolean literal (folded by the caller), or a predicate.</summary>
    private string? Arm(ExpressionSyntax arm, string path, out bool isTrue, out bool isFalse)
    {
        var stripped = Strip(arm);
        isTrue = stripped.IsKind(SyntaxKind.TrueLiteralExpression);
        isFalse = stripped.IsKind(SyntaxKind.FalseLiteralExpression);
        return isTrue || isFalse ? "" : Translate(arm, path, asPredicate: true);
    }
}
