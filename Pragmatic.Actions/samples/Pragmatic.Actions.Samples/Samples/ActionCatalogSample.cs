namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     Catalog of all action patterns demonstrated in this project.
///     Actions require the full DI pipeline (Invoker, filters, UnitOfWork) —
///     this sample describes what's available and what the SG generates.
/// </summary>
public static class ActionCatalogSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. Action Pattern Catalog — What the SG Generates");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  DomainAction Variants:");
        Console.WriteLine("  ─────────────────────────");
        Console.WriteLine("    PlaceOrderAction         : DomainAction<Guid, ValidationError>");
        Console.WriteLine("                               → 2 deps (IOrderRepository, IEmailService)");
        Console.WriteLine("                               → SG: SetDependencies + Invoker + DI registration");
        Console.WriteLine();
        Console.WriteLine("    GetOrderAction           : DomainAction<OrderRecord, NotFoundError>");
        Console.WriteLine("                               → 1 dep, returns structured DTO or 404");
        Console.WriteLine();
        Console.WriteLine("    ComputeTotalAction       : DomainAction<decimal>");
        Console.WriteLine("                               → 0 deps, pure computation");
        Console.WriteLine("                               → SG: Invoker only (no SetDependencies)");
        Console.WriteLine();

        Console.WriteLine("  VoidDomainAction Variants:");
        Console.WriteLine("  ─────────────────────────────");
        Console.WriteLine("    SendOrderConfirmationAction : VoidDomainAction");
        Console.WriteLine("                                  → Uses Success/Failure convenience properties");
        Console.WriteLine();
        Console.WriteLine("    ArchiveOrderAction          : VoidDomainAction<NotFoundError>");
        Console.WriteLine("                                  → Void with typed error (404 response)");
        Console.WriteLine();

        Console.WriteLine("  Advanced Patterns:");
        Console.WriteLine("  ──────────────────────");
        Console.WriteLine("    ClaimAwareAction         : [FromClaim(\"sub\")] UserId + optional TenantId");
        Console.WriteLine("                               → JWT claim binding for authenticated APIs");
        Console.WriteLine();
        Console.WriteLine("    VersionedCreateOrderAction : [SinceVersion(\"2.0\")] + ExecuteV2()");
        Console.WriteLine("                                 → API versioning with version-specific logic");
        Console.WriteLine();

        Console.WriteLine("  SG Output per Action:");
        Console.WriteLine("  ─────────────────────────");
        Console.WriteLine("    {Action}.SetDependencies.g.cs  — injects private fields from DI");
        Console.WriteLine("    {Action}.Invoker.g.cs          — sealed nested Invoker class");
        Console.WriteLine("    _Infra.Actions.Registration    — AddPragmaticActions() aggregate");
        Console.WriteLine();
    }
}
