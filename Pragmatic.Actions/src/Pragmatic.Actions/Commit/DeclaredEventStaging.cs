using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Events.Outbox;

namespace Pragmatic.Actions.Commit;

/// <summary>
///     How the invocation that owns the commit hands the events it declares (<c>[Raises&lt;T&gt;]</c>) to an outbox (#736): staged before
///     its save, so the outbox's capture writes them in the save's transaction; taken after the commit, so it dispatches in process only
///     what no outbox took.
/// </summary>
/// <remarks>
///     The stage exists only where an outbox registered it. Without one, <see cref="Stage" /> answers null and the invoker builds and
///     dispatches its events after the commit, as before the outbox could see them.
/// </remarks>
internal static class DeclaredEventStaging
{
    /// <summary>Stages <paramref name="build" /> for the save about to run; null when no outbox is registered.</summary>
    public static DeclaredEventStage? Stage(IServiceProvider services, Func<IReadOnlyList<IDomainEvent>> build)
    {
        var stage = services.GetService<DeclaredEventStage>();
        stage?.Stage(build);
        return stage;
    }

    /// <summary>
    ///     After the commit: what no outbox took, built now; or, with no stage, what <paramref name="build" /> builds, as without an outbox.
    /// </summary>
    public static IReadOnlyList<IDomainEvent> Remaining(DeclaredEventStage? stage, Func<IReadOnlyList<IDomainEvent>> build)
        => stage is null ? build() : stage.Take();
}
