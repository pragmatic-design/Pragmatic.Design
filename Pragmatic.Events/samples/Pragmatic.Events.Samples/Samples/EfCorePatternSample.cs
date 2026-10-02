namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     EF Core integration: the lifecycle interceptor raises, EfCoreUnitOfWork dispatches
///     events after SaveChanges. Conceptual — no actual database.
/// </summary>
public static class EfCorePatternSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. EF Core Integration — lifecycle events");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Setup in DI:");
        Console.WriteLine("  ──────────────");
        Console.WriteLine("""
            services.AddInMemoryDomainEvents();

            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseNpgsql(connectionString);
                options.UseDomainEvents();  // <── raises [Raises<T>] events; EfCoreUnitOfWork dispatches them
            });
        """);
        Console.WriteLine();

        Console.WriteLine("  How it works:");
        Console.WriteLine("  ──────────────");
        Console.WriteLine("    1. Entity calls RaiseEvent() during business logic");
        Console.WriteLine("    2. EF Core tracks the entity (it implements IHasDomainEvents)");
        Console.WriteLine("    3. On IUnitOfWork.SaveChangesAsync():");
        Console.WriteLine("       a. The lifecycle interceptor raises the [Raises<T>] events (SavingChanges)");
        Console.WriteLine("       b. The database commits — a failed save stops here, nothing is taken");
        Console.WriteLine("       c. The events are taken off every tracked entity (taking clears them)");
        Console.WriteLine("       d. EfCoreUnitOfWork dispatches them, outside the commit");
        Console.WriteLine();

        Console.WriteLine("  Example flow:");
        Console.WriteLine("  ──────────────");
        Console.WriteLine("""
            // In your service/handler:
            var order = new Order();
            order.Place("Widget Pro", 5);  // Raises OrderPlacedEvent internally

            dbContext.Orders.Add(order);
            await unitOfWork.SaveChangesAsync(ct);
            // ↑ the unit of work dispatches OrderPlacedEvent to all registered handlers.
            //   A mutation or a domain action does the same through its generated invoker.
            //   The interceptor only raises: a bare DbContext.SaveChangesAsync dispatches nothing.
        """);
        Console.WriteLine();

        Console.WriteLine("  Key behaviors:");
        Console.WriteLine("  ──────────────");
        Console.WriteLine("    - Events dispatched AFTER a successful save, outside the transaction");
        Console.WriteLine("    - Taking the events clears them → a retried save does not dispatch twice");
        Console.WriteLine("    - Multiple entities → events aggregated and dispatched in sequence");
        Console.WriteLine("    - A handler that throws does not roll back the save: it has already committed");
        Console.WriteLine();
    }
}
