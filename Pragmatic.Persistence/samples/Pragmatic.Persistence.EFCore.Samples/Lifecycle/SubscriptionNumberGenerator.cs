using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     Generates sequential subscription numbers (<c>SUB-YYYYMM-NNNNN</c>) at creation time.
///     Implements <see cref="IDefaultValueGenerator{TEntity,TValue}"/> — the contract the host
///     invokes for a property tagged with <c>[ComputedDefault]</c>. Mirrors the
///     <c>[GeneratedValue("SUB-{YYYY}{MM}-{SEQ:5}")]</c> format on <see cref="Subscription"/>.
/// </summary>
public sealed class SubscriptionNumberGenerator : IDefaultValueGenerator<Subscription, string>
{
    private static int _counter;

    public Task<string> GenerateAsync(Subscription entity, LifecycleContext context, CancellationToken ct)
    {
        var seq = Interlocked.Increment(ref _counter);
        return Task.FromResult($"SUB-{context.Now:yyyyMM}-{seq:D5}");
    }
}
