using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     The application functions the generator registered for the runtime path, by method: a renderer per dialect over the
///     arguments' SQL. Registered at module start from the definitions the generator read, like the entity maps.
/// </summary>
public static class SqlFunctions
{
    private static readonly ConcurrentDictionary<MethodInfo, (Func<SqlDialect, string[], string> Render, bool IsPredicate)> s_functions = new();

    public static void Register(MethodInfo method, Func<SqlDialect, string[], string> render, bool isPredicate = false) => s_functions[method] = (render, isPredicate);

    public static bool TryGet(MethodInfo method, out Func<SqlDialect, string[], string> render, out bool isPredicate)
    {
        if (s_functions.TryGetValue(method, out var entry))
        {
            render = entry.Render;
            isPredicate = entry.IsPredicate;
            return true;
        }

        render = null!;
        isPredicate = false;
        return false;
    }
}
