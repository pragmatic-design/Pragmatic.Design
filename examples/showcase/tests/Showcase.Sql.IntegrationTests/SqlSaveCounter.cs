using Pragmatic.Persistence.Sql;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>A save phase that adds nothing to the save and counts how many times it ran (<see cref="CommitCounter" />).</summary>
public sealed class SqlSaveCounter : ISqlSavePhase
{
    public int Count { get; private set; }

    public ValueTask ApplyAsync(SqlSaveContext save, CancellationToken ct)
    {
        Count++;
        return ValueTask.CompletedTask;
    }
}
