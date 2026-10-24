namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>A unit of work's view of a row's columns: its snapshot, and which columns changed since.</summary>
public static class SqlSnapshots
{
    /// <summary>The row's column values as they are bound, in its writer's column order.</summary>
    public static object?[] Take(object row, SqlDialect dialect) => WriterOf(row).Snapshot(row, dialect);

    /// <summary>The columns whose value differs from <paramref name="snapshot" />, a bit each; zero when none.</summary>
    public static ulong ChangedColumns(object row, object?[] snapshot, SqlDialect dialect) => WriterOf(row).ChangedColumns(row, snapshot, dialect);

    /// <summary>The row's own writer: a snapshot is of one table, so a table-per-type row is refused.</summary>
    internal static ISqlWriter WriterOf(object row)
    {
        var map = SqlEntityMap.For(row.GetType()) ?? throw new InvalidOperationException($"No map is registered for {row.GetType().Name}: it is not an [Entity] of a translated assembly.");
        var writer = map.WriterFor(row);
        if (writer.Part is not null)
            throw new NotSupportedException($"{row.GetType().Name} is written to a table per type: a unit of work's snapshot covers one table. Write it with SaveGraphAsync or a write batch.");
        return writer;
    }
}
