using Pragmatic.Persistence.Query.Adapters;

namespace Pragmatic.Persistence.Fuzz;

/// <summary>
///     The runtime PrimeNG adapter on a body a grid client controls (#310), the query run against
///     <see cref="FuzzOrder.Rows" />.
/// </summary>
/// <remarks>
///     The declared outcomes are a query that runs, <see cref="GridFilterTooLargeException" />, and the refusal of a
///     field or a clause the adapter cannot apply (<see cref="GridFieldRejectedException" />,
///     <see cref="GridFilterRejectedException" />, #318). Those return; anything else escapes and is a crash.
/// </remarks>
public static class PrimeNGAdapterTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (GridRequest.Bind<PrimeNGLazyLoadEvent>(data) is not { } lazyEvent)
            return;

        try
        {
            _ = Query.Query.For<FuzzOrder>().FromPrimeNG(lazyEvent).Build(FuzzOrder.Rows()).ToList();
        }
        catch (Exception e) when (e is GridFilterTooLargeException or GridFieldRejectedException or GridFilterRejectedException)
        {
        }
    }
}
