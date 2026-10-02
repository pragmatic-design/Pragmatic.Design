namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Frozen pane configuration — rows and/or columns frozen at the top-left corner.
/// </summary>
public readonly record struct FrozenPane(int Rows = 0, int Columns = 0);
