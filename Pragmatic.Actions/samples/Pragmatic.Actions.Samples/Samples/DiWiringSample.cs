namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     DI wiring: how actions are registered and invoked in the Pragmatic pipeline.
///     Shows DI setup, invoker resolution, and IStartupStep integration.
/// </summary>
public static class DiWiringSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. DI Wiring — Registration & Invocation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  4.1 SG-generated aggregate registration");
        Console.WriteLine("  -------------------------------------------");
        Console.WriteLine("""
            // In IStartupStep.ConfigureServices():
            services.AddPragmaticActions();  // Registers ALL invokers in this assembly

            // This is equivalent to:
            // services.AddScoped<IDomainActionInvoker<PlaceOrderAction, Guid>,
            //                    PlaceOrderAction.Invoker>();
            // services.AddScoped<IDomainActionInvoker<GetOrderAction, OrderRecord>,
            //                    GetOrderAction.Invoker>();
            // ... one per action
        """);
        Console.WriteLine();

        Console.WriteLine("  4.2 Invoker usage in services");
        Console.WriteLine("  ---------------------------------");
        Console.WriteLine("""
            public class OrderService(
                IDomainActionInvoker<PlaceOrderAction, Guid> placeOrder,
                IVoidDomainActionInvoker<ArchiveOrderAction> archiveOrder)
            {
                public async Task<Result<Guid, IError>> PlaceAsync(string product, int qty, CancellationToken ct)
                {
                    var action = new PlaceOrderAction { Product = product, Quantity = qty };
                    return await placeOrder.InvokeAsync(action, ct);
                    // Invoker: resolves deps → validates → executes → saves → returns
                }
            }
        """);
        Console.WriteLine();

        Console.WriteLine("  4.3 Sub-boundary interface (SG-generated)");
        Console.WriteLine("  ─────────────────────────────────────────────");
        Console.WriteLine("""
            // SG generates typed interfaces per sub-boundary:
            public interface IBookingGuestsActions
            {
                Task<Result<Guest, IError>> CreateGuest(CreateGuestMutation mutation, CancellationToken ct);
                Task<Result<Guest, IError>> UpdateGuest(UpdateGuestMutation mutation, CancellationToken ct);
            }

            // ISP: inject only the sub-boundary you need
            public class GuestService(IBookingGuestsActions guests)
            {
                public Task<Result<Guest, IError>> CreateAsync(...)
                    => guests.CreateGuest(new CreateGuestMutation { ... }, ct);
            }
        """);
        Console.WriteLine();
    }
}
