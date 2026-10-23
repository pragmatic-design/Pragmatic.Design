using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql;

/// <summary>The path of a runtime Include lambda, read from its tree: the properties walked, and on a collection its filter, orderings and Take.</summary>
internal static partial class DynamicIncludes
{
    /// <summary>One property of the path; on a collection, what the LINQ operators written on it say.</summary>
    private sealed class PathPart(string name)
    {
        public string Name { get; } = name;

        public LambdaExpression? Filter { get; set; }

        public List<(LambdaExpression Key, bool Descending)> Orderings { get; } = [];

        public int? Take { get; set; }
    }

    /// <summary>
    ///     The steps a path walks from its parameter, root first: member accesses through casts, a <c>Then</c> call as what its receiver
    ///     reaches followed by the path of its lambda from that lambda's own parameter, and <c>Enumerable.Where</c> / <c>OrderBy</c> /
    ///     <c>OrderByDescending</c> / <c>ThenBy</c> / <c>ThenByDescending</c> / <c>Take</c> as the filter, ordering and Take of the collection
    ///     their receiver reaches (the ones before applied first). False when the expression is something else.
    /// </summary>
    private static bool Names(Expression node, ParameterExpression parameter, List<PathPart> names)
    {
        var own = new List<string>();
        while (true)
        {
            switch (node)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.Quote } unary:
                    node = unary.Operand;
                    continue;
                case MemberExpression { Expression: { } owner } member:
                    own.Insert(0, member.Member.Name);
                    node = owner;
                    continue;
                case MethodCallExpression { Method: { Name: "Then", DeclaringType: var declaring }, Arguments.Count: 2 } then when declaring == typeof(SqlPath) && Unquote(then.Arguments[1]) is LambdaExpression below:
                    if (!Names(then.Arguments[0], parameter, names) || !Names(below.Body, below.Parameters[0], names))
                        return false;
                    names.AddRange(own.Select(o => new PathPart(o)));
                    return true;
                case MethodCallExpression { Method.DeclaringType: var declaring, Arguments.Count: 2 } call when declaring == typeof(Enumerable) && call.Method.Name is "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Take":
                {
                    if (own.Count > 0 || !Names(call.Arguments[0], parameter, names) || names.Count == 0)
                        return false;
                    var last = names[names.Count - 1];
                    var argument = Unquote(call.Arguments[1]);
                    switch (call.Method.Name)
                    {
                        case "Where" when argument is LambdaExpression { Parameters.Count: 1 } filter && last.Filter is null:
                            last.Filter = filter;
                            return true;
                        case "OrderBy" or "OrderByDescending" when argument is LambdaExpression { Parameters.Count: 1 } key:
                            last.Orderings.Clear();
                            last.Orderings.Add((key, call.Method.Name == "OrderByDescending"));
                            return true;
                        case "ThenBy" or "ThenByDescending" when argument is LambdaExpression { Parameters.Count: 1 } key && last.Orderings.Count > 0:
                            last.Orderings.Add((key, call.Method.Name == "ThenByDescending"));
                            return true;
                        case "Take":
                            last.Take = argument is ConstantExpression { Value: int n } ? n : 0; // a value of the caller is reported as not positive: the plan is shared
                            return true;
                        default:
                            return false;
                    }
                }

                case ParameterExpression root when root == parameter:
                    names.AddRange(own.Select(o => new PathPart(o)));
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>True when the tree reads a closure (a captured local or field): the compiler puts those behind a ConstantExpression of the closure's class.</summary>
    private static bool HasCaptures(Expression body)
    {
        var finder = new CaptureFinder();
        finder.Visit(body);
        return finder.Found;
    }

    private sealed class CaptureFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is not null && node.Type.IsClass && node.Type != typeof(string))
                Found = true;
            return node;
        }
    }

    private static Expression Unquote(Expression expression) => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

    private static RuntimeInclude Insert(List<RuntimeInclude> level, RuntimeInclude node)
    {
        level.Add(node);
        return node;
    }
}
