using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>One id sequence for every plan of every row type: a static field of the generic class would be one per type, and two plans would share a cached command.</summary>
internal static class SqlPlanIds
{
    private static int s_next;

    public static int Next() => Interlocked.Increment(ref s_next) - 1;
}
