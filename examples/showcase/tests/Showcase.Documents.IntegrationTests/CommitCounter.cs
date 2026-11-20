using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Documents;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     How many times a boundary's unit of work saved in one DI scope.
/// </summary>
/// <remarks>
///     The documents engine's copy. It replaces the suite's <c>Infrastructure/CommitCounter.cs</c>, which counts the <c>DbContext</c>'s
///     <c>SavingChanges</c>: here a save phase keyed by the boundary, which the generated registration adds to that boundary's unit of work
///     only. A save with nothing to write returns before its phases, so what is counted is a save that reached the store.
/// </remarks>
public static class CommitCounter
{
    /// <summary>A counting phase on each of <paramref name="boundaries" />, one per scope.</summary>
    public static void Register(IServiceCollection services, params Type[] boundaries)
    {
        foreach (var boundary in boundaries)
        {
            services.AddKeyedScoped<DocSaveCounter>(boundary);
            services.AddKeyedScoped<IDocSavePhase>(boundary, (sp, key) => sp.GetRequiredKeyedService<DocSaveCounter>(key));
        }
    }

    /// <summary>The saves of <paramref name="boundary" />'s unit of work in <paramref name="scope" />.</summary>
    public static Func<int> Watch(IServiceProvider scope, Type boundary)
    {
        var counter = scope.GetRequiredKeyedService<DocSaveCounter>(boundary);
        return () => counter.Count;
    }
}
