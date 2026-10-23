using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A join's result selector read as a shape: each member of the anonymous object or the DTO initializer is the row it names
///     (a parameter: that parameter's shape; <c>x.Order</c>: a member carried forward; <c>c.Customer</c>: a navigation, joined),
///     or a value rendered as SQL. The shape is what the parameter of the next operator stands for.
/// </summary>
internal sealed partial class LambdaTranslator
{
    /// <summary>The shape of a selector body: a composite for an object, a parameter's own shape, an entity reached by navigation, or a rendered value. Null with <see cref="Error" />.</summary>
    public RowShape? FromSelector(LambdaExpressionSyntax lambda, ITypeSymbol resultType)
    {
        var body = Strip(SqlInterceptorGenerator.Body(lambda));
        switch (body)
        {
            case AnonymousObjectCreationExpressionSyntax anonymous:
            {
                var members = new List<(string, RowShape)>();
                foreach (var initializer in anonymous.Initializers)
                {
                    var name = initializer.NameEquals?.Name.Identifier.ValueText ?? (Strip(initializer.Expression) as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? (Strip(initializer.Expression) as IdentifierNameSyntax)?.Identifier.ValueText;
                    if (name is null)
                    {
                        Fail(initializer, "an anonymous member needs a name");
                        return null;
                    }
                    var shape = ShapeOf(initializer.Expression);
                    if (shape is null)
                        return null;
                    members.Add((name, shape));
                }

                return new CompositeRow(members, resultType);
            }

            case ObjectCreationExpressionSyntax { Initializer: { } initializer } creation when creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0:
            {
                var members = new List<(string, RowShape)>();
                foreach (var expression in initializer.Expressions)
                {
                    if (expression is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment)
                    {
                        Fail(expression, "only 'Member = value' assignments are translated in a join's result");
                        return null;
                    }

                    var shape = ShapeOf(assignment.Right);
                    if (shape is null)
                        return null;
                    members.Add((target.Identifier.ValueText, shape));
                }

                return new CompositeRow(members, resultType);
            }

            default:
                return ShapeOf(body);
        }
    }

    /// <summary>The shape of one expression of a selector.</summary>
    public RowShape? ShapeOf(ExpressionSyntax expression)
    {
        var written = expression; // the type as written: a cast to the nullable type ((decimal?)o.Total) is how a LEFT JOIN's value declares it may be null
        expression = Strip(expression);
        if (expression is IdentifierNameSyntax identifier && _model.GetSymbolInfo(identifier).Symbol is IParameterSymbol p)
        {
            if (SymbolEqualityComparer.Default.Equals(p, _parameter))
                return _row;
            if (_joined is not null && SymbolEqualityComparer.Default.Equals(p, _joined._parameter))
                return _joined._row;
        }

        if (expression is MemberAccessExpressionSyntax member && (IsOnParameter(member) || IsOnJoined(member)))
        {
            var joined = IsOnJoined(member);
            var translator = joined ? _joined! : this;
            var segments = translator.Segments(member)!;
            var at = translator.EntityAt(segments, member, out var rendered);
            if (at is null && rendered is null)
            {
                if (translator._lastGroup is { } group)
                    return group; // a GroupJoin's group carried forward, or named by the SelectMany that joins it
                if (translator._error is not null)
                    Fail(member, translator._error);
                return null;
            }

            if (at is not null)
            {
                var (entity, alias, start, optional, row) = at.Value;
                if (start == segments.Count)
                    return row; // the entity itself, carried forward
                // Navigations up to the last segment keep the row an entity; a last segment that is a navigation is an entity row too.
                if (!row.Derived)
                {
                    var current = entity;
                    var currentAlias = alias;
                    var currentOptional = optional;
                    var i = start;
                    for (; i < segments.Count; i++)
                    {
                        var navigation = current.Navigation(segments[i]);
                        var target = navigation is null ? null : _entities(navigation.Target);
                        if (navigation is null || target is null)
                            break;
                        var next = AliasOf(currentAlias, segments[i]);
                        var join = current.Join(navigation, target, currentAlias, next) + (translator.MarkJoin?.Invoke(target, next) ?? "");
                        if (!translator._joins.Contains(join))
                            translator._joins.Add(join);
                        current = target;
                        currentAlias = next;
                        currentOptional |= !navigation.Required;
                    }

                    if (i == segments.Count)
                        return new EntityRow(current, currentAlias, currentOptional || (joined && _joinedOptional));
                }
            }
        }

        // A value: rendered as SQL, read with its type's reader (nullable when the join may leave it absent).
        var type = _model.GetTypeInfo(written).Type;
        if (type is null)
        {
            Fail(expression, "the member's type could not be resolved");
            return null;
        }

        var sql = ColumnOf(expression);
        if (sql is null)
            return null;
        if (ParameterPaths.Count > 0)
        {
            Fail(expression, "a join's result carries columns and constants, not values of the caller");
            return null;
        }

        string? reader;
        if (LastColumnOptional)
        {
            if (!EntityModel.IsNullable(type) && type.IsValueType)
            {
                Fail(expression, $"a LEFT JOIN reads the inner row's columns as null: cast the member to its nullable type (({type.ToDisplayString()}?){expression})");
                return null;
            }

            reader = EntityModel.ReaderFor(type, forceNullable: true);
        }
        else
        {
            reader = LastColumn is { } source ? source.ProjectedReader(type) : EntityModel.ReaderFor(type);
        }

        if (reader is null)
        {
            Fail(expression, $"type {type.ToDisplayString()} is not read");
            return null;
        }

        return new ColumnRow(sql, type, reader);
    }
}
