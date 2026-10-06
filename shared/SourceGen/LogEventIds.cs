// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>The event id of a call site that does not set one.</summary>
/// <remarks>
///     Derived from the event name with FNV-1a over its UTF-16 code units, so the same name gives the same
///     id on every build, machine and runtime — <c>string.GetHashCode</c> is randomized per process and
///     would give a different id each time the host starts. Two call sites of one type that derive the
///     same id are reported by the analyzer, as two that set the same one are.
/// </remarks>
internal static class LogEventIds
{
    public static int Derive(string eventName)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in eventName)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
