namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     Advanced patterns: CompositeAction, LoadEntity, query integration.
/// </summary>
public static class AdvancedPatternsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Advanced — CompositeAction, LoadEntity, Queries");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowCompositeAction();
        ShowLoadEntity();
        ShowQueryPattern();

        Console.WriteLine();
    }

    private static void ShowCompositeAction()
    {
        Console.WriteLine("  5.1 [CompositeAction] — Multi-mutation transaction");
        Console.WriteLine("  -----------------------------------------------------");

        Console.WriteLine("""
            [DomainAction, CompositeAction]
            public partial class PlaceOrderComposite : DomainAction<OrderConfirmation, IError>
            {
                private IBookingReservationsActions _reservations = null!;
                private IBillingInvoicesActions _invoices = null!;

                public required CreateReservationMutation Reservation { get; init; }
                public required CreateInvoiceMutation Invoice { get; init; }

                public override async Task<Result<OrderConfirmation, IError>> Execute(CancellationToken ct)
                {
                    // Both mutations run in same transaction (single SaveChanges)
                    var reservation = await _reservations.Create(Reservation, ct);
                    if (reservation.IsFailure) return reservation.Error;

                    var invoice = await _invoices.Create(Invoice, ct);
                    if (invoice.IsFailure) return invoice.Error;

                    return new OrderConfirmation(reservation.Value.Id, invoice.Value.Id);
                }
            }

            // CompositeAction: all mutations share the same UnitOfWork scope.
            // If any mutation fails, everything rolls back.
        """);
        Console.WriteLine();
    }

    private static void ShowLoadEntity()
    {
        Console.WriteLine("  5.2 [LoadEntity<T>] — Auto-load entity before execute");
        Console.WriteLine("  --------------------------------------------------------");

        Console.WriteLine("""
            [DomainAction]
            [LoadEntity<Order>(nameof(OrderId))]
            public partial class CancelOrderMutation : Mutation<Order, NotFoundError>
            {
                public required Guid OrderId { get; init; }
                public required string Reason { get; init; }

                public override Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct)
                {
                    entity.Cancel(Reason);
                    return Task.FromResult(Result<Order, IError>.Success(entity));
                }
            }

            // LoadEntity automatically:
            // 1. Reads OrderId from the mutation
            // 2. Calls repository.GetByIdAsync(OrderId)
            // 3. Returns NotFoundError if not found
            // 4. Passes loaded entity to ApplyToEntity/ApplyAsync
        """);
        Console.WriteLine();
    }

    private static void ShowQueryPattern()
    {
        Console.WriteLine("  5.3 Query pattern — [Query<T>] with filtering and pagination");
        Console.WriteLine("  ---------------------------------------------------------------");

        Console.WriteLine("""
            [Query<Order>]
            [Endpoint(HttpVerb.Get, "/orders")]
            public partial class SearchOrdersQuery
            {
                public string? Status { get; init; }

                [Range(1, 100)]
                public int PageSize { get; init; } = 20;

                public int Page { get; init; } = 1;
            }

            // SG generates Apply() method that builds IQueryable<Order>:
            //   .Where(o => Status == null || o.Status == Status)
            //   .Skip((Page - 1) * PageSize)
            //   .Take(PageSize)
            //
            // Exposed as GET /orders?status=Pending&page=1&pageSize=20
        """);
        Console.WriteLine();
    }
}
