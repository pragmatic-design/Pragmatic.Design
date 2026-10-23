namespace Pragmatic.Persistence.Sql.Generator;

internal sealed partial class EntityModel
{
    /// <summary>The <c>[FullText]</c> columns get the table they are written to and its key column: what the native entries name (FTS5's table, CONTAINSTABLE's key).</summary>
    private void MarkFullText()
    {
        foreach (var column in Columns)
        {
            if (!column.FullText || Keys.Count != 1)
                continue;
            column.FullTextTable = WriteTable ?? Table;
            column.FullTextKey = KeyColumn;
        }
    }
}
