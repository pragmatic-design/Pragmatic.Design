using System.Buffers;
using System.Globalization;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     Walks a runtime chain the way <see cref="ExpressionTranslator" /> will, producing a key for its shape and the
///     leaves (the sub-trees the translator turns into parameter values) without translating anything: no SQL text,
///     no joins, no reflection. The second call with the same shape finds its plan by the key and reads only the
///     leaves. A construct the translator rejects, or that this reader does not mirror, gives no key: the planner
///     then translates as it always did. One reader per thread, reused: the key is written into a pooled buffer and
///     looked up as a span, so a hit allocates nothing of its own.
/// </summary>
internal sealed partial class ShapeReader
{
    [ThreadStatic]
    private static ShapeReader? t_reader;

    private SqlEntityMap _map = null!;
    private char[] _buffer = ArrayPool<char>.Shared.Rent(512);
    private int _length;
    private ParameterExpression? _parameter;
    private bool _supported = true;

    private ShapeReader()
    {
    }

    /// <summary>The captured sub-trees, in the order met; a <see cref="CaptureSource" /> indexes them.</summary>
    public List<Expression> Leaves { get; } = new(8);

    /// <summary>The entity's rule trees built for this call: the planner translates these same objects, so their leaves are found.</summary>
    public List<LambdaExpression> Rules { get; } = [];

    /// <summary>The key written so far; valid until the next <see cref="Rent" /> on this thread.</summary>
    public ReadOnlySpan<char> Key => _buffer.AsSpan(0, _length);

    /// <summary>This thread's reader, reset for a chain on <paramref name="map" />. The planner reads a chain synchronously, so no two chains share it.</summary>
    public static ShapeReader Rent(SqlEntityMap map)
    {
        var reader = t_reader ??= new ShapeReader();
        reader._map = map;
        reader._length = 0;
        reader._supported = true;
        reader._parameter = null;
        reader.Leaves.Clear();
        reader.Rules.Clear();
        return reader;
    }

    /// <summary>True when the whole chain is in the mirrored subset: <see cref="Key" /> and <see cref="Leaves" /> then describe it.</summary>
    public bool Read(List<MethodCallExpression> steps, SqlDialect dialect, string terminal, LambdaExpression? argument)
    {
        Append((int)dialect).Append('|').Append(_map.Type.FullName).Append('|');
        foreach (var step in steps)
        {
            var name = step.Method.Name;
            Append(name).Append(':');
            switch (name)
            {
                case "Where":
                    Body(Lambda(step.Arguments[1]), asPredicate: true);
                    break;
                case "OrderBy":
                case "OrderByDescending":
                case "ThenBy":
                case "ThenByDescending":
                    Body(Lambda(step.Arguments[1]), asPredicate: false);
                    break;
                case "Skip":
                case "Take":
                case "After": // the cursor: one leaf, read once per ordering key (the planner's converters take the items)
                case "Before":
                    Leaf(step.Arguments[1], SqlCaptureKind.Value);
                    break;
                case "AsNoTracking":
                case "Project":
                case "Distinct":
                    break;
                case "Include":
                    // The whole path is the shape (members, Then steps, a collection's filter): it captures nothing, a filter holds constants only.
                    IncludeShape(Lambda(step.Arguments[1]).Body);
                    break;
                case "Select":
                {
                    // A column read as a scalar is translated; any other body is the DTO's declared projection, whose text the tree does not shape.
                    var lambda = Lambda(step.Arguments[1]);
                    if (lambda.Body is MemberExpression member && lambda.Parameters[0] == Root(member) && _map.Column(member.Member.Name) is not null)
                        Body(lambda, asPredicate: false);
                    else
                        Append("dto");
                    break;
                }

                default:
                    _supported = false;
                    break;
            }

            Append(';');
        }

        foreach (var rule in _map.Rules)
        {
            var lambda = rule();
            Rules.Add(lambda);
            Append("rule:");
            Body(lambda, asPredicate: true);
            Append(';');
        }

        Append('|').Append(terminal);
        if (argument is not null && terminal is "FirstOrDefaultAsync" or "CountAsync" or "AnyAsync")
        {
            Append(':');
            Body(argument, asPredicate: true);
        }
        else if (argument is not null && terminal is "SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync")
        {
            Append(':');
            Body(argument, asPredicate: false);
        }

        return _supported;
    }

    // The key's characters, into the pooled buffer (grown when a long chain needs it).

    private ShapeReader Append(char c)
    {
        if (_length == _buffer.Length)
            Grow(1);
        _buffer[_length++] = c;
        return this;
    }

    private ShapeReader Append(string? text)
    {
        if (text is null)
            return this;
        if (_length + text.Length > _buffer.Length)
            Grow(text.Length);
        text.AsSpan().CopyTo(_buffer.AsSpan(_length));
        _length += text.Length;
        return this;
    }

    private ShapeReader Append(int value)
    {
        if (_length + 11 > _buffer.Length)
            Grow(11);
        value.TryFormat(_buffer.AsSpan(_length), out var written, default, CultureInfo.InvariantCulture);
        _length += written;
        return this;
    }

    private void Grow(int more)
    {
        var bigger = ArrayPool<char>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + more));
        _buffer.AsSpan(0, _length).CopyTo(bigger);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = bigger;
    }

    private void Body(LambdaExpression lambda, bool asPredicate)
    {
        _parameter = lambda.Parameters[0];
        Visit(lambda.Body, asPredicate);
    }

    /// <summary>An Include path, root first: <c>p.Orders.Then(p.Lines)</c>, <c>p.Orders.Where[filter text]</c>. Two paths through the same collection with different steps below are two shapes.</summary>
    private void IncludeShape(Expression node)
    {
        node = Strip(node);
        switch (node)
        {
            case ParameterExpression:
                Append('p');
                return;
            case MemberExpression member when member.Expression is not null:
                IncludeShape(member.Expression);
                Append('.').Append(member.Member.Name);
                return;
            case MethodCallExpression { Method: { Name: "Then", DeclaringType: var declaring }, Arguments.Count: 2 } then when declaring == typeof(SqlPath) && Strip(then.Arguments[1]) is LambdaExpression below:
                IncludeShape(then.Arguments[0]);
                Append(".Then(");
                IncludeShape(below.Body);
                Append(')');
                return;
            case MethodCallExpression { Method: { Name: "Where", DeclaringType: var declaring }, Arguments.Count: 2 } where when declaring == typeof(Enumerable) && Strip(where.Arguments[1]) is LambdaExpression filter:
                IncludeShape(where.Arguments[0]);
                Append(".Where[").Append(filter.ToString()).Append(']'); // constants only (DynamicIncludes refuses captures): the text is the filter's identity
                return;
            default:
                _supported = false;
                return;
        }
    }

    private void Leaf(Expression node, SqlCaptureKind kind)
    {
        // The static type is part of the shape: the same text with an int or a decimal captured types the parameter differently.
        Append(kind == SqlCaptureKind.Value ? '?' : kind == SqlCaptureKind.Like ? '~' : '@').Append(node.Type.Name);
        Leaves.Add(node);
    }

    private void Path(MemberExpression member)
    {
        if (Strip(member.Expression!) is MemberExpression owner)
        {
            Path(owner);
            Append('.');
        }

        Append(member.Member.Name);
    }

    private bool IsOnParameter(MemberExpression member)
    {
        Expression? root = member;
        while (root is MemberExpression inner)
            root = Strip(inner.Expression!);
        return root == _parameter;
    }

    private static Expression Root(MemberExpression member)
    {
        Expression node = member;
        while (node is MemberExpression inner)
            node = inner.Expression!;
        return node;
    }

    private static Expression Strip(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote } unary)
            node = unary.Operand;
        return node;
    }

    private static bool IsNull(Expression node) => Strip(node) is ConstantExpression { Value: null };

    private static LambdaExpression Lambda(Expression argument)
        => argument is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } ? quoted : argument as LambdaExpression ?? throw new NotSupportedException($"'{argument}' is not a lambda.");

    /// <summary>True when the lambda parameter appears below: the node is a column expression, not a captured value. The common node kinds without a visitor; the rest through one.</summary>
    private bool DependsOnParameter(Expression node) => node switch
    {
        ParameterExpression => node == _parameter,
        ConstantExpression => false,
        MemberExpression member => member.Expression is not null && DependsOnParameter(member.Expression),
        UnaryExpression unary => DependsOnParameter(unary.Operand),
        BinaryExpression binary => DependsOnParameter(binary.Left) || DependsOnParameter(binary.Right),
        MethodCallExpression call => (call.Object is not null && DependsOnParameter(call.Object)) || Any(call.Arguments),
        NewArrayExpression array => Any(array.Expressions),
        _ => new ParameterFinder(_parameter!).Found(node),
    };

    private bool Any(IReadOnlyList<Expression> nodes)
    {
        foreach (var node in nodes)
        {
            if (DependsOnParameter(node))
                return true;
        }

        return false;
    }

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        private bool _found;

        public bool Found(Expression node)
        {
            Visit(node);
            return _found;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == parameter)
                _found = true;
            return node;
        }
    }
}
