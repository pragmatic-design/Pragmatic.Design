using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql;

/// <summary>The arguments a query chain received, one per operator that took one (<c>Where((o, arg) => …, arg)</c>), in chain order; read by the plan when it binds the parameters.</summary>
[InlineArray(SqlQuery.MaxSlots)]
public struct SqlSlots
{
    private object? _element0;
}
