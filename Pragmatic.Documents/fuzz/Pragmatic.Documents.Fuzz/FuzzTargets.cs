using SharpFuzz;

namespace Pragmatic.Documents.Fuzz;

/// <summary>
///     The harnesses of this project, by the name <c>fuzz.json</c> and <c>scripts/fuzz.mjs</c> use for them. A test
///     checks that the two lists are the same.
/// </summary>
public static class FuzzTargets
{
    public static IReadOnlyDictionary<string, ReadOnlySpanAction> All { get; } = new Dictionary<string, ReadOnlySpanAction>
    {
        ["xlsx-reader"] = XlsxReaderTarget.Run,
    };
}
