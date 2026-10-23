using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

// One file, two assemblies (the documents bridge links it): a namespace each, or a project seeing both engines has the type twice (CS0433).
#if PRAGMATIC_DOCUMENTS
namespace Pragmatic.Persistence.Documents.Pragmatic;
#else
namespace Pragmatic.Persistence.Sql;
#endif

/// <summary>
///     The values a lambda captured, read off the expression tree the compiler built for it. The
///     generator knows at compile time which sub-expression is a captured variable (a member of the
///     closure, a field, a property chain, a constant); at runtime the interceptor walks exactly that
///     node and reads it. No compilation, no interpretation of operators: only constants and member
///     reads. <c>FieldInfo.GetValue</c> on a member the tree itself references is reflection on metadata
///     the compiler emitted and the trimmer keeps; whether Native AOT keeps it is the first thing the
///     spike tests (see the AOT smoke).
/// </summary>
public static class Captured
{
    /// <summary>The value of <paramref name="expression" />, which must be a constant or a member chain rooted in one.</summary>
    public static object? Evaluate(Expression expression)
    {
        switch (expression)
        {
            case ConstantExpression constant:
                return constant.Value;
            case MemberExpression member:
                var target = member.Expression is null ? null : Evaluate(member.Expression);
                return member.Member switch
                {
                    FieldInfo field => field.GetValue(target),
                    PropertyInfo property => property.GetValue(target),
                    _ => throw NotCaptured(expression),
                };
            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary:
                return Evaluate(unary.Operand); // enum → underlying, T → T?: the database takes the raw value
            case NewArrayExpression array:
                var items = new object?[array.Expressions.Count];
                for (var i = 0; i < items.Length; i++)
                    items[i] = Evaluate(array.Expressions[i]);
                return items;
            case MethodCallExpression { Method.Name: "op_Implicit" or "op_Explicit", Arguments.Count: 1 } conversion:
                return Evaluate(conversion.Arguments[0]); // a user or span conversion the compiler inserted (an array passed as ReadOnlySpan<T>): the raw value serves
            case MethodCallExpression call:
            {
                // A method on captured values (text.ToLower(), since.AddDays(30), Guid.Parse(s)): computed here, in .NET, and
                // sent as a value. The tree references the method itself, so the metadata is there, Native AOT included.
                var receiver = call.Object is null ? null : Evaluate(call.Object);
                if (call.Object is not null && receiver is null)
                    return null;
                var arguments = new object?[call.Arguments.Count];
                for (var i = 0; i < arguments.Length; i++)
                    arguments[i] = Evaluate(call.Arguments[i]);
                return call.Method.Invoke(receiver, arguments);
            }

            default:
                throw NotCaptured(expression);
        }
    }

    /// <summary>The pattern for a LIKE: the captured text between the wildcards the generator chose.</summary>
    public static string Like(Expression expression, string prefix, string suffix)
        => prefix + (Evaluate(expression)?.ToString() ?? "") + suffix;

    /// <summary>A captured collection, for an IN list.</summary>
    public static IEnumerable List(Expression expression) => Evaluate(expression) as IEnumerable ?? Array.Empty<object>();

    // Paths into a lambda, as the generator emits them: the body, then an operand at a time.

    public static Expression Body(LambdaExpression lambda) => lambda.Body;

    public static Expression Left(Expression expression) => ((BinaryExpression)Unwrap(expression)).Left;

    public static Expression Right(Expression expression) => ((BinaryExpression)Unwrap(expression)).Right;

    public static Expression Operand(Expression expression) => ((UnaryExpression)Unwrap(expression)).Operand;

    public static Expression Argument(Expression expression, int index) => ((MethodCallExpression)Unwrap(expression)).Arguments[index];

    public static Expression Target(Expression expression) => ((MethodCallExpression)Unwrap(expression)).Object!;

    public static Expression MemberOf(Expression expression, int index) => ((MemberInitExpression)Unwrap(expression)).Bindings[index] is MemberAssignment assignment ? assignment.Expression : throw NotCaptured(expression);

    public static Expression NewArgument(Expression expression, int index) => ((NewExpression)Unwrap(expression)).Arguments[index];

    /// <summary>The body of a lambda passed as an argument (a collection predicate inside the tree), quoted or not.</summary>
    public static Expression Lambda(Expression expression) => ((LambdaExpression)(expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression)).Body;

    public static Expression Test(Expression expression) => ((ConditionalExpression)Unwrap(expression)).Test;

    public static Expression IfTrue(Expression expression) => ((ConditionalExpression)Unwrap(expression)).IfTrue;

    public static Expression IfFalse(Expression expression) => ((ConditionalExpression)Unwrap(expression)).IfFalse;

    /// <summary>The compiler wraps some operands in a Convert (an enum compared to a constant, a lifted nullable): the structure below is what the generator saw.</summary>
    private static Expression Unwrap(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary ? Unwrap(unary.Operand) : expression;

    private static NotSupportedException NotCaptured(Expression expression)
        => new($"The expression '{expression}' is not a captured value: only constants, variables and member chains can be parameters. The generator should have reported it (PRAG3002).");
}
