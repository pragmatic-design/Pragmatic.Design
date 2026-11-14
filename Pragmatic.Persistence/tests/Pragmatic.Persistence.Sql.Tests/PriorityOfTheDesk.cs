using Pragmatic.Persistence.Lifecycle;

namespace PragmaticEntities.Tests;

/// <summary>A <c>[ComputedDefault]</c> of a value type, from the same scoped service.</summary>
public sealed class PriorityOfTheDesk(CallerDesk desk) : IDefaultValueGenerator<SupportCase, int>
{
    public Task<int> GenerateAsync(SupportCase entity, LifecycleContext context, CancellationToken ct) => Task.FromResult(desk.Priority);
}
