using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     How many times a boundary's unit of work saved in one DI scope.
/// </summary>
/// <remarks>
///     The EF Core copy: the boundary <c>DbContext</c>'s <c>SavingChanges</c>, raised by every save of that scope. Each engine suite replaces
///     this file with its own, which counts through a save phase of the boundary instead, since the engines have no <c>DbContext</c>.
/// </remarks>
public static class CommitCounter
{
    /// <summary>
    ///     What the counter needs registered on the host, for the boundaries it will watch. Nothing on EF Core: the <c>DbContext</c> reports
    ///     its own saves.
    /// </summary>
    public static void Register(IServiceCollection services, params Type[] boundaries)
    {
    }

    /// <summary>The saves of <paramref name="boundary" />'s unit of work in <paramref name="scope" /> from now on.</summary>
    public static Func<int> Watch(IServiceProvider scope, Type boundary)
    {
        var db = scope.GetRequiredKeyedService<DbContext>(boundary);
        var commits = 0;
        db.SavingChanges += (_, _) => commits++;
        return () => commits;
    }
}
