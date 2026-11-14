using Pragmatic.Persistence.Lifecycle;

namespace PragmaticDocuments.Tests;

/// <summary>A <c>[ComputedDefault]</c> generator that reads a scoped service.</summary>
public sealed class ReceptionDeskName(ReceptionDesk desk) : IDefaultValueGenerator<SupportCase, string>
{
    public Task<string> GenerateAsync(SupportCase entity, LifecycleContext context, CancellationToken ct) => Task.FromResult(desk.Name);
}
