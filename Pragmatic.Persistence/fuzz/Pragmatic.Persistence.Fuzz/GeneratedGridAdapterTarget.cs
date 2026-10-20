using Pragmatic.Persistence.Query.Adapters;

namespace Pragmatic.Persistence.Fuzz;

/// <summary>
///     The adapter the generator writes (<see cref="FuzzOrderGrid" />) on a body a grid client controls (#310), read
///     as both formats, the query run against <see cref="FuzzOrder.Rows" />.
/// </summary>
/// <remarks>
///     The declared outcomes are a query that runs, <see cref="GridFilterTooLargeException" />, and the refusal of a
///     field or a clause the adapter cannot apply (<see cref="GridFieldRejectedException" />,
///     <see cref="GridFilterRejectedException" />, #318). Those return; anything else escapes and is a crash. The
///     generated code is in this assembly, which is not instrumented: it runs, and a crash in it is found, but
///     libFuzzer's coverage does not steer toward it.
/// </remarks>
public static class GeneratedGridAdapterTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (GridRequest.Bind<DevExpressLoadOptions>(data) is { } options)
            Refusable(() => FuzzOrderGrid.Apply(FuzzOrder.Rows(), options).ToList());

        if (GridRequest.Bind<PrimeNGLazyLoadEvent>(data) is { } lazyEvent)
            Refusable(() => FuzzOrderGrid.Apply(FuzzOrder.Rows(), lazyEvent).ToList());
    }

    private static void Refusable(Func<List<FuzzOrder>> query)
    {
        try
        {
            _ = query();
        }
        catch (Exception e) when (e is GridFilterTooLargeException or GridFieldRejectedException or GridFilterRejectedException)
        {
        }
    }
}
