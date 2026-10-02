using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Shared helpers for safely enumerating IEnumerable with proper disposal.
/// </summary>
internal static class EnumerableHelper
{
    /// <summary>
    ///     Checks whether a non-generic IEnumerable has at least one element,
    ///     properly disposing the enumerator if it implements IDisposable.
    /// </summary>
    internal static bool HasAny(IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    ///     Counts the elements in a non-generic IEnumerable,
    ///     properly disposing the enumerator if it implements IDisposable.
    /// </summary>
    internal static int Count(IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        try
        {
            var count = 0;
            while (enumerator.MoveNext())
                count++;
            return count;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
