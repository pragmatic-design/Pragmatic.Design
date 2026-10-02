using System.Collections.Concurrent;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     Holds the parsed translation data for a single culture.
///     Shared by <see cref="JsonLocalizationProvider"/> and <see cref="InMemoryLocalizationProvider"/>.
/// </summary>
internal sealed record CultureData(
    IDictionary<string, string> Strings,
    IDictionary<string, PluralString> Plurals);

internal static class CultureDataFactory
{
    /// <summary>Creates an immutable snapshot for read-heavy providers (e.g. JSON).</summary>
    internal static CultureData CreateSnapshot(
        Dictionary<string, string> strings,
        Dictionary<string, PluralString> plurals)
        => new(strings, plurals);

    /// <summary>Creates a mutable, thread-safe instance for in-memory providers.</summary>
    internal static CultureData CreateConcurrent()
        => new(
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal),
            new ConcurrentDictionary<string, PluralString>(StringComparer.Ordinal));
}
