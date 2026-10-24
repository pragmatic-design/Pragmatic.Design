using System.Globalization;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     The runtime twin of the generator's translator: the same rules, applied to an expression tree built
///     at runtime (a grid filter, a query composed from options). Columns come from the registered entity
///     map; everything else that is not an operator is a captured value, read through the planner's
///     <see cref="SqlCapture" /> (which notes where it sat, for the shape cache) and sent as a parameter.
///     Nothing is compiled: the tree is only read.
/// </summary>
internal sealed partial class ExpressionTranslator
{
    private readonly SqlEntityMap _map;
    private readonly ParameterExpression _parameter;
    private readonly SqlDialect _dialect;
    // By PropertyInfo: the string overloads of Expression.Property need unreferenced code. Taken from a lambda the compiler builds, so the
    // property is the compiler's token rather than a lookup by name at run time.
    private static readonly System.Reflection.PropertyInfo RangeFrom = PropertyOf(r => r.From);
    private static readonly System.Reflection.PropertyInfo RangeTo = PropertyOf(r => r.To);
    private static System.Reflection.PropertyInfo PropertyOf(Expression<Func<SqlDateRange, DateOnly?>> property)
        => (System.Reflection.PropertyInfo)((MemberExpression)property.Body).Member;

    private readonly List<object?> _values;
    private readonly List<SqlParamKind> _kinds;
    private readonly List<string> _joins;
    private readonly HashSet<string>? _innerAliases;
    private readonly SqlCapture _capture;
    private int _orDepth; // > 0 inside an OR or a NOT: a row may be wanted without its joined row there
    private NullTest _nullTest;
    private SqlColumn? _convertedColumn; // inside a comparison with a converted column: the value on the other side binds through its converter

    /// <summary>The last column a member access translated to (an ordering key's, for a cursor's converter and kind).</summary>
    public SqlColumn? LastColumn { get; private set; }

    private enum NullTest
    {
        None,
        IsNull,
        IsNotNull,
    }

    /// <summary><paramref name="dialect" /> renders the catalog functions; <paramref name="innerAliases" /> collects the navigations this predicate needs a joined row for (the generator's rule): their LEFT JOIN becomes INNER.</summary>
    public ExpressionTranslator(SqlEntityMap map, ParameterExpression parameter, SqlDialect dialect, List<object?> values, List<SqlParamKind> kinds, List<string> joins, SqlCapture? capture = null, HashSet<string>? innerAliases = null, string alias = "t")
    {
        _map = map;
        _alias = alias;
        _parameter = parameter;
        _dialect = dialect;
        _values = values;
        _kinds = kinds;
        _joins = joins;
        _capture = capture ?? CaptureSource.Direct;
        _innerAliases = innerAliases;
    }

    /// <summary>A boolean body as a SQL predicate; the values it captured are appended to the shared lists.</summary>
    public string Predicate(Expression body) => Translate(body, asPredicate: true);

    /// <summary>A member body as a SQL column.</summary>
    public string Column(Expression body) => Translate(body, asPredicate: false);

    private string Translate(Expression node, bool asPredicate)
    {
        node = Strip(node);
        switch (node)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } logical:
            {
                var or = logical.NodeType == ExpressionType.OrElse;
                if (or)
                    _orDepth++;
                var text = $"({Translate(logical.Left, true)}) {(or ? "OR" : "AND")} ({Translate(logical.Right, true)})";
                if (or)
                    _orDepth--;
                return text;
            }

            case BinaryExpression binary:
                return Binary(binary);

            case UnaryExpression { NodeType: ExpressionType.Not } not:
            {
                _orDepth++;
                var text = $"NOT ({Translate(not.Operand, true)})";
                _orDepth--;
                return text;
            }

            case UnaryExpression { NodeType: ExpressionType.ArrayLength, Operand: MemberExpression array } when IsOnParameter(array) && Segments(array) is { Count: 1 } one && _map.Column(one[0]) is { Kind: SqlParamKind.Array } arrayColumn:
                return ArrayColumnLength(_alias, arrayColumn); // x.Tags.Length on an array column (a tree has ArrayLength, not a member access)

            case not ConstantExpression when asPredicate && node.Type == typeof(bool) && !DependsOnParameter(node):
                // A boolean of the caller's where a predicate is expected (a permission of the request's context): compared with true, as a bare boolean column is.
                return $"{Parameter(_capture(node, SqlCaptureKind.Value, "", "", null))} = {Parameter(_capture(null, SqlCaptureKind.True, "", "", null))}";

            case MemberExpression member when IsOnParameter(member):
            {
                var column = ColumnOf(member);
                if (asPredicate && member.Type == typeof(bool))
                    return $"{column} = {Parameter(_capture(null, SqlCaptureKind.True, "", "", null))}"; // a bare boolean column: SQL Server has no boolean expressions
                return column;
            }

            case MethodCallExpression call:
                return Invocation(call);

            case ConditionalExpression conditional:
                if (asPredicate)
                    throw Unsupported(node, "a conditional (?:) yields a value: write the condition with && and || instead");
                return $"CASE WHEN {Translate(conditional.Test, true)} THEN {Translate(conditional.IfTrue, false)} ELSE {Translate(conditional.IfFalse, false)} END";

            default:
                if (!DependsOnParameter(node))
                    return Parameter(_capture(node, SqlCaptureKind.Value, "", "", _convertedColumn?.ToStore), _convertedColumn?.Kind);
                throw Unsupported(node, $"a {node.NodeType} is not in the translated subset");
        }
    }

    private static Expression Strip(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote } unary)
            node = unary.Operand;
        return node;
    }

    private string Binary(BinaryExpression binary)
    {
        if (binary.NodeType == ExpressionType.Coalesce)
            return $"COALESCE({Translate(binary.Left, false)}, {Translate(binary.Right, false)})";
        var op = binary.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.Add => "+",
            ExpressionType.Subtract => "-",
            ExpressionType.Multiply => "*",
            ExpressionType.Divide => "/",
            _ => throw Unsupported(binary, $"operator {binary.NodeType} is not translated"),
        };
        var leftNull = IsNull(binary.Left);
        var rightNull = IsNull(binary.Right);
        if ((leftNull || rightNull) && binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            _nullTest = binary.NodeType == ExpressionType.Equal ? NullTest.IsNull : NullTest.IsNotNull;
            var operand = Translate(leftNull ? binary.Right : binary.Left, false);
            _nullTest = NullTest.None;
            return $"{operand} IS {(binary.NodeType == ExpressionType.Equal ? "" : "NOT ")}NULL";
        }

        // A converted column on one side: the value on the other binds through the converter, as the column's store kind.
        _convertedColumn = ConvertedColumnOf(binary.Left) ?? ConvertedColumnOf(binary.Right);
        var text = $"{Translate(binary.Left, false)} {op} {Translate(binary.Right, false)}";
        _convertedColumn = null;
        return text;
    }

    private SqlColumn? ConvertedColumnOf(Expression side)
    {
        if (Strip(side) is not MemberExpression { Expression: { } owner } member)
            return null;
        owner = Strip(owner);
        var path = owner == _parameter ? member.Member.Name : owner is MemberExpression hop && Strip(hop.Expression!) == _parameter ? $"{hop.Member.Name}.{member.Member.Name}" : null;
        return path is not null && _map.Column(path) is { ToStore: not null } column ? column : null;
    }

    private static bool IsNull(Expression node) => Strip(node) is ConstantExpression { Value: null };

    private string Invocation(MethodCallExpression call)
    {
        var method = call.Method;
        if (SqlFunctions.TryGet(method, out var render, out _))
        {
            // An application function the generator registered: its arguments rendered, its template for this dialect.
            var rendered = new string[call.Arguments.Count];
            for (var i = 0; i < rendered.Length; i++)
                rendered[i] = Translate(call.Arguments[i], false);
            return render(_dialect, rendered);
        }

        if (method.DeclaringType != typeof(Sql) && !DependsOnParameter(call))
            return Parameter(_capture(call, SqlCaptureKind.Value, "", "", _convertedColumn?.ToStore), _convertedColumn?.Kind); // a method on captured values: computed in .NET, sent as a value
        if (method.DeclaringType == typeof(Enumerable) && method.Name is "Any" or "All" or "Count" && call.Arguments.Count >= 1 && DependsOnParameter(call.Arguments[0]))
            return ArrayColumnOverlaps(call) ?? JsonArrayAny(call) ?? CollectionPredicate(call); // x.Tags.Any(t => list.Contains(t)) on an array column; d.Spec.Chapters.Any(c => …) over a JSON array; o.Lines.Any(l => …): a correlated subquery on the child's table
        if (method.DeclaringType == typeof(Sql))
        {
            // The typed catalog: the same rendering the generator emits, from the shared SqlCatalog; a DatePart argument is a constant read off the tree.
            var parameters = method.GetParameters();
            var name = parameters.Length > 0 && parameters[^1].ParameterType == typeof(SqlWindow) ? method.Name + "Window" : method.Name; // over a named window
            var entry = SqlCatalog.Find(name, call.Arguments.Count) ?? throw Unsupported(call, $"Sql.{method.Name} with {call.Arguments.Count} arguments is not in the catalog");
            if (entry.Name is "FullText" or "FullTextRank" && call.Arguments.Count == 2)
            {
                // A [FullText] column: the native entries, with its table, name and key (FTS5 on SQLite, CONTAINS on SQL Server).
                var column = Translate(call.Arguments[0], false);
                var marked = LastColumn;
                if (marked is not { FullTextTable: { } table, FullTextKey: { } key })
                    return entry.Render(_dialect, [column, Translate(call.Arguments[1], false)]);
                // The query bound as a SqlFullTextQuery: the engine's condition, every word required.
                var query = Parameter(_capture(call.Arguments[1], SqlCaptureKind.Value, "", "", static v => SqlFullTextQuery.Of(v)), SqlParamKind.String);
                return SqlCatalog.Find(entry.Name + "Native", 5)!.Render(_dialect, [column, query, table, marked.Column, key]);
            }

            var rendered = new string[call.Arguments.Count];
            for (var i = 0; i < rendered.Length; i++)
            {
                rendered[i] = i == entry.PartIndex
                    ? (Captured.Evaluate(call.Arguments[i]) as DatePart? ?? throw Unsupported(call, "the date part must be a constant (DatePart.Day)")).ToString()
                    : i == entry.LiteralIndex
                        ? (Captured.Evaluate(call.Arguments[i]) is int count and >= 0 ? count : throw Unsupported(call, $"Sql.{method.Name}'s count must be a non-negative integer")).ToString(CultureInfo.InvariantCulture)
                        : Translate(call.Arguments[i], false);
            }

            return entry.Render(_dialect, rendered);
        }

        if (method.DeclaringType == typeof(SqlDateRange) && method.Name == "Contains" && call.Object is not null && call.Arguments.Count == 1)
        {
            // x.Range.Contains(day): the two bound columns of the owned range and the day, through the catalog's range entry.
            var range = SqlCatalog.Find("RangeContains", 3)!;
            return range.Render(_dialect, [Translate(Expression.Property(call.Object, RangeFrom), false), Translate(Expression.Property(call.Object, RangeTo), false), Translate(call.Arguments[0], false)]);
        }

        if (method.DeclaringType == typeof(string) && call.Object is not null)
        {
            switch (method.Name)
            {
                case "Contains" or "StartsWith" or "EndsWith" when call.Arguments.Count == 1:
                {
                    var column = Translate(call.Object, false);
                    if (DependsOnParameter(call.Arguments[0]))
                        throw Unsupported(call, "the text of a LIKE must be a value, not a column");
                    var (prefix, suffix) = method.Name switch { "Contains" => ("%", "%"), "StartsWith" => ("", "%"), _ => ("%", "") };
                    return $"{column} LIKE {Parameter(_capture(call.Arguments[0], SqlCaptureKind.Like, prefix, suffix, null))}";
                }

                case "ToLower" or "ToLowerInvariant" when call.Arguments.Count == 0:
                    return $"LOWER({Translate(call.Object, false)})";
                case "ToUpper" or "ToUpperInvariant" when call.Arguments.Count == 0:
                    return $"UPPER({Translate(call.Object, false)})";
            }
        }

        if (method.DeclaringType == typeof(string) && method.Name == "IsNullOrEmpty" && call.Arguments.Count == 1)
        {
            var column = Translate(call.Arguments[0], false);
            return $"({column} IS NULL OR {column} = '')";
        }

        // collection.Contains(column) → IN: an instance method (List<T>.Contains) or Enumerable.Contains(list, x).
        if (method.Name == "Contains")
        {
            var collection = call.Object ?? (call.Arguments.Count == 2 ? call.Arguments[0] : null);
            var item = call.Object is not null ? call.Arguments[0] : call.Arguments.Count == 2 ? call.Arguments[1] : null;
            if (collection is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } span)
                collection = span.Arguments[0]; // C# 14 binds array.Contains(x) to MemoryExtensions.Contains over the array as a span
            if (collection is not null && item is not null && DependsOnParameter(collection) && JsonArrayContains(collection, item) is { } jsonArray)
                return jsonArray; // an element of a JSON array of the row
            if (collection is not null && item is not null && DependsOnParameter(collection) && ArrayColumnContains(collection, item) is { } arrayColumn)
                return arrayColumn; // an element of an array column of the row
            if (collection is not null && item is not null && !DependsOnParameter(collection))
                return $"{Translate(item, false)} {(_dialect == SqlDialect.PostgreSql ? "= ANY(" : "IN (")}{Parameter(_capture(collection, SqlCaptureKind.List, "", "", null), SqlParamKind.List)})"; // an array parameter on PostgreSQL, the list expanded elsewhere
        }

        throw Unsupported(call, $"method '{method.Name}' is not translated; compute it before the query");
    }

    private bool IsOnParameter(MemberExpression member)
    {
        Expression? root = member;
        while (root is MemberExpression inner)
            root = Strip(inner.Expression!);
        return root == _parameter;
    }

    /// <summary>True when the lambda parameter appears anywhere below: the node is not a captured value.</summary>
    private bool DependsOnParameter(Expression node) => new ParameterFinder(_parameter).Found(node);

    private string Parameter(object? value, SqlParamKind? kind = null)
    {
        _values.Add(value);
        _kinds.Add(kind ?? SqlValue.KindOf(value));
        return "{" + (_values.Count - 1).ToString(CultureInfo.InvariantCulture) + "}";
    }

    private static NotSupportedException Unsupported(Expression node, string why)
        => new($"'{node}' cannot be translated at runtime: {why}.");

    private sealed class ParameterFinder : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;
        private bool _found;

        public ParameterFinder(ParameterExpression parameter) => _parameter = parameter;

        public bool Found(Expression node)
        {
            Visit(node);
            return _found;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _parameter)
                _found = true;
            return node;
        }
    }
}
