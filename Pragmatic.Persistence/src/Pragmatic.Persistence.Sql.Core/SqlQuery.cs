using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql;

public static class SqlQuery
{
    /// <summary>How many argument-taking operators one chain may have; the generator reports a longer chain.</summary>
    public const int MaxSlots = 8;
}
