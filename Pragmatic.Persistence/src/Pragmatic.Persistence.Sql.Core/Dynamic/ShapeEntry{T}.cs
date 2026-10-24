using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>A shape already translated: its plan, and where each parameter's value sits in a tree of that shape.</summary>
internal sealed class ShapeEntry<T>(SqlPlan<T> plan, CaptureSource[] sources)
{
    public SqlPlan<T> Plan { get; } = plan;

    public object?[] Values(List<Expression> leaves)
    {
        var values = new object?[sources.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = sources[i].Read(leaves);
        return values;
    }
}
