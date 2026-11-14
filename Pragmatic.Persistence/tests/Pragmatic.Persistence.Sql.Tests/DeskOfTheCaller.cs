using Pragmatic.Persistence.Lifecycle;

namespace PragmaticEntities.Tests;

/// <summary>A <c>[ComputedDefault]</c> generator that reads a scoped service: the desk of the request's caller.</summary>
public sealed class DeskOfTheCaller(CallerDesk desk) : IDefaultValueGenerator<SupportCase, string>
{
    public Task<string> GenerateAsync(SupportCase entity, LifecycleContext context, CancellationToken ct) => Task.FromResult(desk.Name);
}
