using Pragmatic.Caching;

namespace Warehouse.Stock.Infrastructure.Caching;

/// <summary>
///     The tags of the cached availability read, and the one way an operation that moved stock drops them.
/// </summary>
/// <remarks>
///     <para>
///         One tag per product: a movement drops the availability of what it moved and of nothing else. With a
///         single tag for every product, each receipt, hold or expiry emptied the whole cache on both
///         instances — including the expiry job of every order confirmed in time, which gives back nothing.
///     </para>
///     <para>
///         The read without a product filter answers for every product, so its tag is the prefix alone
///         (<see cref="Unfiltered" />, what <see cref="OfProductTemplate" /> renders with no product), and
///         every movement drops it as well.
///     </para>
/// </remarks>
internal static class AvailabilityCache
{
    /// <summary>The tag of the read without a product filter.</summary>
    public const string Unfiltered = "availability:";

    /// <summary>The tag of one product's read, as an attribute writes it: <c>{ProductId}</c> is filled in.</summary>
    public const string OfProductTemplate = Unfiltered + "{ProductId}";

    /// <summary>The tag of one product's read, as <see cref="OfProductTemplate" /> renders it.</summary>
    public static string Of(Guid productId) => $"{Unfiltered}{productId}";

    /// <summary>
    ///     Drops the availability of <paramref name="moved" /> and the unfiltered read — or nothing, when no
    ///     product moved.
    /// </summary>
    public static async ValueTask DropAsync(ICacheStack cache, ICollection<Guid> moved, CancellationToken ct)
    {
        if (moved.Count == 0)
            return;

        foreach (var product in moved)
            await cache.InvalidateByTagAsync(Of(product), ct).ConfigureAwait(false);

        await cache.InvalidateByTagAsync(Unfiltered, ct).ConfigureAwait(false);
    }
}
