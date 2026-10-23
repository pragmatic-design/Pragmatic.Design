using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql;

/// <summary>The shape reader's visitors: the nodes of a predicate or a key, the method calls, and an Include path.</summary>
internal sealed partial class ShapeReader
{
    private void Visit(Expression node, bool asPredicate)
    {
        if (!_supported)
            return;
        node = Strip(node);
        switch (node)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } logical:
                Append(logical.NodeType == ExpressionType.AndAlso ? "&(" : "|(");
                Visit(logical.Left, true);
                Append(',');
                Visit(logical.Right, true);
                Append(')');
                return;

            case BinaryExpression binary:
            {
                if (binary.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Coalesce))
                {
                    _supported = false;
                    return;
                }

                var leftNull = IsNull(binary.Left);
                var rightNull = IsNull(binary.Right);
                Append((int)binary.NodeType).Append('(');
                if ((leftNull || rightNull) && binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
                {
                    Append("null,");
                    Visit(leftNull ? binary.Right : binary.Left, false);
                }
                else
                {
                    Visit(binary.Left, false);
                    Append(',');
                    Visit(binary.Right, false);
                }

                Append(')');
                return;
            }

            case UnaryExpression { NodeType: ExpressionType.Not } not:
                Append("!(");
                Visit(not.Operand, true);
                Append(')');
                return;

            case MemberExpression member when IsOnParameter(member):
                Append("m:");
                Path(member);
                if (asPredicate && member.Type == typeof(bool))
                    Append("=T"); // the translator binds the constant true here: a parameter with no leaf
                return;

            case MethodCallExpression call:
                Invocation(call);
                return;

            case ConditionalExpression conditional:
                Append("?(");
                Visit(conditional.Test, true);
                Append(',');
                Visit(conditional.IfTrue, false);
                Append(',');
                Visit(conditional.IfFalse, false);
                Append(')');
                return;

            default:
                if (!DependsOnParameter(node))
                    Leaf(node, SqlCaptureKind.Value);
                else
                    _supported = false;
                return;
        }
    }

    private void Invocation(MethodCallExpression call)
    {
        var method = call.Method;
        if (SqlFunctions.TryGet(method, out _, out _))
        {
            // An application function: its name and its arguments shape the key.
            Append("fn:").Append(method.DeclaringType?.Name).Append('.').Append(method.Name).Append('(');
            foreach (var argument in call.Arguments)
            {
                Visit(argument, false);
                Append(',');
            }

            Append(')');
            return;
        }

        if (method.DeclaringType != typeof(Sql) && !DependsOnParameter(call))
        {
            Leaf(call, SqlCaptureKind.Value);
            return;
        }

        if (method.DeclaringType == typeof(Enumerable) && method.Name is "Any" or "All" or "Count" && call.Arguments.Count >= 1 && call.Arguments[0] is MemberExpression owned && IsOnParameter(owned))
        {
            // A collection predicate: the collection's path, then the inner lambda's body with the inner parameter as the row.
            Append(method.Name).Append('(');
            Path(owned);
            if (call.Arguments.Count > 1 && Strip(call.Arguments[1]) is LambdaExpression inner)
            {
                var outer = _parameter;
                _parameter = inner.Parameters[0];
                Append(':');
                Visit(inner.Body, true);
                _parameter = outer;
            }

            Append(')');
            return;
        }

        if (method.DeclaringType == typeof(Sql))
        {
            // A catalog function: its name and its arguments shape the key; a DatePart is a constant of the shape, the other arguments are visited (columns, leaves).
            var entry = SqlCatalog.Find(method.Name, call.Arguments.Count);
            if (entry is null)
            {
                _supported = false;
                return;
            }

            Append("Sql.").Append(method.Name).Append('(');
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                if (i == entry.PartIndex || i == entry.LiteralIndex) // written into the statement: a constant of the shape
                    Append(Captured.Evaluate(call.Arguments[i])?.ToString());
                else
                    Visit(call.Arguments[i], false);
                Append(',');
            }

            Append(')');
            return;
        }

        if (method.DeclaringType == typeof(string) && call.Object is not null)
        {
            switch (method.Name)
            {
                case "Contains" or "StartsWith" or "EndsWith" when call.Arguments.Count == 1:
                    Append(method.Name).Append('(');
                    Visit(call.Object, false);
                    if (DependsOnParameter(call.Arguments[0]))
                    {
                        _supported = false;
                        return;
                    }

                    Leaf(call.Arguments[0], SqlCaptureKind.Like);
                    Append(')');
                    return;

                case "ToLower" or "ToLowerInvariant" when call.Arguments.Count == 0:
                case "ToUpper" or "ToUpperInvariant" when call.Arguments.Count == 0:
                    Append(method.Name).Append('(');
                    Visit(call.Object, false);
                    Append(')');
                    return;
            }
        }

        if (method.DeclaringType == typeof(string) && method.Name == "IsNullOrEmpty" && call.Arguments.Count == 1)
        {
            Append("nullOrEmpty(");
            Visit(call.Arguments[0], false);
            Append(')');
            return;
        }

        if (method.Name == "Contains")
        {
            var collection = call.Object ?? (call.Arguments.Count == 2 ? call.Arguments[0] : null);
            var item = call.Object is not null ? call.Arguments[0] : call.Arguments.Count == 2 ? call.Arguments[1] : null;
            if (collection is not null && item is not null && !DependsOnParameter(collection))
            {
                Append("in(");
                Visit(item, false);
                Leaf(collection, SqlCaptureKind.List);
                Append(')');
                return;
            }
        }

        _supported = false;
    }
}
