namespace Pragmatic.Persistence.Benchmarks.Workload;

/// <summary>The parent a roll-up rule sums <see cref="Ticket.Amount" /> into.</summary>
public sealed class Project
{
    public Guid Id { get; set; }
    public decimal Total { get; private set; }

    public void ApplyDelta(decimal delta) => Total += delta;
}
