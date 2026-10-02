namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     Mutation patterns: entity CRUD via typed mutation DTOs.
///     SG generates ApplyToEntity() which calls entity setters.
/// </summary>
public static class MutationPatternSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Mutation Patterns — Entity CRUD via Typed DTOs");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowCreateMutation();
        ShowUpdateMutation();
        ShowMutationPipeline();

        Console.WriteLine();
    }

    private static void ShowCreateMutation()
    {
        Console.WriteLine("  3.1 Create mutation — auto-mapped properties");
        Console.WriteLine("  ------------------------------------------------");

        Console.WriteLine("""
            [DomainAction]
            public partial class CreateGuestMutation : Mutation<Guest>
            {
                [Required, MinLength(2)]
                public required string FirstName { get; init; }

                [Required, MinLength(2)]
                public required string LastName { get; init; }

                [Email]
                public string? Email { get; init; }
            }

            // SG generates:
            // CreateGuestMutation.ApplyToEntity.g.cs
            //   → entity.SetFirstName(this.FirstName);
            //   → entity.SetLastName(this.LastName);
            //   → if (this.Email is { } email) entity.SetEmail(email);
            //
            // CreateGuestMutation.Invoker.g.cs
            //   → MutationInvoker pipeline: Validate → Create entity → ApplyToEntity → Save
        """);
        Console.WriteLine();
    }

    private static void ShowUpdateMutation()
    {
        Console.WriteLine("  3.2 Update mutation — ApplyAsync override for custom logic");
        Console.WriteLine("  -------------------------------------------------------------");

        Console.WriteLine("""
            [DomainAction]
            [LoadEntity<Reservation>(nameof(ReservationId))]
            public partial class ConfirmReservationMutation : Mutation<Reservation>
            {
                public required Guid ReservationId { get; init; }

                // Override ApplyAsync for state transitions
                public override Task<Result<Reservation, IError>> ApplyAsync(
                    Reservation entity, CancellationToken ct)
                {
                    if (entity.Status != ReservationStatus.Pending)
                        return Task.FromResult(
                            Result<Reservation, IError>.Failure(
                                new BusinessRuleError("Only pending reservations can be confirmed")));

                    entity.Confirm();  // Domain method
                    return Task.FromResult(Result<Reservation, IError>.Success(entity));
                }
            }

            // Pipeline: LoadEntity → Validate → ApplyToEntity (auto) → ApplyAsync (manual) → Save
            // Auto-mapped properties run BEFORE ApplyAsync, dev override adds custom logic AFTER.
        """);
        Console.WriteLine();
    }

    private static void ShowMutationPipeline()
    {
        Console.WriteLine("  3.3 Mutation invoker pipeline");
        Console.WriteLine("  --------------------------------");

        Console.WriteLine("    1. Resolve dependencies (SetDependencies)");
        Console.WriteLine("    2. LoadEntity (if [LoadEntity<T>]) or Create new entity");
        Console.WriteLine("    3. Sync validation (ISyncValidator from attributes)");
        Console.WriteLine("    4. Async validation (IAsyncValidator from [Validator])");
        Console.WriteLine("    5. ApplyToEntity() — SG-generated, calls entity SetXxx()");
        Console.WriteLine("    6. ApplyAsync() — dev override for custom logic");
        Console.WriteLine("    7. Entity validation (IValidator<TEntity>)");
        Console.WriteLine("    8. Save (IUnitOfWork.SaveChangesAsync)");
        Console.WriteLine("    9. Dispatch domain events (if entity has events)");
        Console.WriteLine("   10. Return Result<TEntity, IError>");
        Console.WriteLine();
    }
}
