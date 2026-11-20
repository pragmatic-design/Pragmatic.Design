using Pragmatic.Persistence.Documents;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>A save phase that writes nothing and counts how many times it ran (<see cref="CommitCounter" />).</summary>
public sealed class DocSaveCounter : IDocSavePhase
{
    public int Count { get; private set; }

    public ValueTask ApplyAsync(DocSaveContext save, CancellationToken ct)
    {
        Count++;
        return ValueTask.CompletedTask;
    }
}
