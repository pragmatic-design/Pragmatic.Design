namespace Pragmatic.Patch.Samples.Samples;

/// <summary>
///     Shows how Patch integrates with HTTP endpoints — conceptual patterns.
/// </summary>
public static class EndpointPatternSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Endpoint Integration — HTTP PATCH Patterns");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  3.1 Minimal API endpoint");
        Console.WriteLine("  ---------------------------");
        Console.WriteLine("""
            app.MapPatch("/guests/{id}", (Guid id, PatchGuest patch) =>
            {
                var guest = repository.GetById(id);
                patch.ApplyTo(guest);
                repository.Save(guest);
                return Results.Ok(new { guest.Id, Modified = patch.ModifiedProperties });
            });
        """);
        Console.WriteLine();

        Console.WriteLine("  3.2 JSON tri-state behavior");
        Console.WriteLine("  ─────────────────────────────");
        Console.WriteLine("    Request: { \"email\": \"new@test.com\" }");
        Console.WriteLine("    → Email.HasValue=true, FirstName.IsUndefined=true");
        Console.WriteLine();
        Console.WriteLine("    Request: { \"email\": null }");
        Console.WriteLine("    → Email.HasValue=true (Null state — clears the field)");
        Console.WriteLine();
        Console.WriteLine("    Request: { }");
        Console.WriteLine("    → All properties Undefined — nothing applied");
        Console.WriteLine();

        Console.WriteLine("  3.3 Pragmatic.Endpoints integration");
        Console.WriteLine("  ──────────────────────────────────────");
        Console.WriteLine("""
            [Endpoint(HttpVerb.Patch, "/{id:guid}")]
            [EndpointGroup<GuestsGroup>]
            public partial class PatchGuestEndpoint : Endpoint<PatchResult>
            {
                [FromBody]
                public required PatchGuest Patch { get; init; }

                public override async Task<Result<PatchResult>> HandleAsync(CancellationToken ct)
                {
                    var entity = await _repository.GetByIdAsync(Id, ct);
                    Patch.ApplyTo(entity);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return new PatchResult(entity.Id, Patch.ModifiedProperties.ToList());
                }
            }
        """);
        Console.WriteLine();
    }
}
