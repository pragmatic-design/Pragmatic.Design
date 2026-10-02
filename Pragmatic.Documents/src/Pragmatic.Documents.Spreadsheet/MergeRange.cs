namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// A merged cell range using A1-style references (e.g. "A1" to "C1").
/// </summary>
public readonly record struct MergeRange(string From, string To);
