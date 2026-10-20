using Pragmatic.Persistence.Query.Adapters;

namespace Pragmatic.Persistence.Fuzz;

/// <summary>
///     The runtime DevExpress adapter on a body a grid client controls (#310), the query run against
///     <see cref="FuzzOrder.Rows" />.
/// </summary>
/// <remarks>
///     The declared outcomes are a query that runs, <see cref="GridFilterTooLargeException" />, and the refusal of a
///     field or a clause the adapter cannot apply (<see cref="GridFieldRejectedException" />,
///     <see cref="GridFilterRejectedException" />, #318). Those return; anything else escapes and is a crash. A stack
///     overflow is a crash libFuzzer sees too: the process dies.
/// </remarks>
public static class DevExpressAdapterTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (GridRequest.Bind<DevExpressLoadOptions>(data) is not { } options)
            return;

        try
        {
            _ = Query.Query.For<FuzzOrder>().FromDevExpress(options).Build(FuzzOrder.Rows()).ToList();
        }
        catch (Exception e) when (e is GridFilterTooLargeException or GridFieldRejectedException or GridFilterRejectedException)
        {
        }
    }
}
