using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>Reads the value a captured node stands for (through the column's converter, when the value is compared with a converted column); the planner records where it came from so the next call with the same shape reads only that.</summary>
internal delegate object? SqlCapture(Expression? node, SqlCaptureKind kind, string prefix, string suffix, Func<object?, object?>? convert);
