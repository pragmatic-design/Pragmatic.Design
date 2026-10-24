using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>One parameter of a translated shape: which leaf of the tree it is read from, how, and through which converter; or the constant it always is.</summary>
internal readonly record struct CaptureSource(int Leaf, SqlCaptureKind Kind, string Prefix, string Suffix, object? Constant = null, Func<object?, object?>? Convert = null)
{
    public object? Read(List<Expression> leaves)
    {
        var value = Kind switch
        {
            SqlCaptureKind.True => true,
            SqlCaptureKind.Constant => Constant,
            SqlCaptureKind.Like => Captured.Like(leaves[Leaf], Prefix, Suffix),
            SqlCaptureKind.List => Captured.List(leaves[Leaf]),
            _ => Captured.Evaluate(leaves[Leaf]),
        };
        return Convert is null ? value : Convert(value);
    }

    /// <summary>The direct read, for a chain translated without the shape cache.</summary>
    public static object? Direct(Expression? node, SqlCaptureKind kind, string prefix, string suffix, Func<object?, object?>? convert)
    {
        var value = kind switch
        {
            SqlCaptureKind.True => true,
            SqlCaptureKind.Like => Captured.Like(node!, prefix, suffix),
            SqlCaptureKind.List => Captured.List(node!),
            _ => Captured.Evaluate(node!),
        };
        return convert is null ? value : convert(value);
    }
}
