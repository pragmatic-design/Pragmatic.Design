using Pragmatic.Composition.Attributes;

namespace Conformance.Sales.Infrastructure.Services;

/// <summary>
///     Counts an effect that sits <b>outside</b> the transaction: an HTTP call, a file, a mail sent by
///     hand.
/// </summary>
/// <remarks>
///     It exists for the re-execution rule: with retry on, a transient fault reruns the body of a
///     transactional operation, and what the body does outside the transaction happens again. The
///     control case measures that on the happy path it happens <b>once</b>.
/// </remarks>
[Service(Lifetime = Lifetime.Singleton, AsSelf = true)]
public sealed class ExternalEffectCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}
