using Pragmatic.Identity;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     Claims access, extension methods, and runtime user inspection.
/// </summary>
public static class ClaimsAndExtensionsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Claims & Extensions — Runtime User Inspection");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowAnonymousExtensions();
        ShowSystemExtensions();
        ShowUsagePatterns();

        Console.WriteLine();
    }

    private static void ShowAnonymousExtensions()
    {
        Console.WriteLine("  3.1 Extension methods on AnonymousUser");
        Console.WriteLine("  ------------------------------------------");

        ICurrentUser anon = AnonymousUser.Instance;
        Console.WriteLine($"    IdOrNull():        {(anon.IdOrNull() is null ? "(null)" : anon.IdOrNull())}");
        Console.WriteLine($"    DisplayNameOrId(): \"{anon.DisplayNameOrId()}\"");
        Console.WriteLine($"    Claims count:      {anon.Claims.Count}");
        Console.WriteLine();
    }

    private static void ShowSystemExtensions()
    {
        Console.WriteLine("  3.2 Extension methods on SystemUser");
        Console.WriteLine("  ----------------------------------------");

        ICurrentUser system = SystemUser.Instance;
        Console.WriteLine($"    IdOrNull():        \"{system.IdOrNull()}\"");
        Console.WriteLine($"    DisplayNameOrId(): \"{system.DisplayNameOrId()}\"");
        Console.WriteLine($"    IsAuthenticated:   {system.IsAuthenticated}");
        Console.WriteLine($"    Kind:              {system.Kind}");
        Console.WriteLine();

        // Authorization checks — SystemUser has full access
        Console.WriteLine("  3.3 SystemUser authorization — full access");
        Console.WriteLine("  -----------------------------------------------");
        Console.WriteLine($"    HasPermission(\"any.perm\"): {system.Authorization.HasPermission("any.permission.at.all")}");
        Console.WriteLine($"    IsInRole(\"any-role\"):      {system.Authorization.IsInRole("any-role")}");
        Console.WriteLine($"    IsInGroup(\"any-group\"):    {system.Authorization.IsInGroup("any-group")}");
        Console.WriteLine($"    All checks return true — SystemUser bypasses authorization.");
        Console.WriteLine();
    }

    private static void ShowUsagePatterns()
    {
        Console.WriteLine("  3.4 Common usage patterns");
        Console.WriteLine("  ----------------------------");

        Console.WriteLine("""
            // Guard: require authenticated user
            if (!currentUser.IsAuthenticated)
                return Result.Failure(new UnauthorizedError());

            // Check specific permission
            if (!currentUser.Authorization.HasPermission("billing.invoice.create"))
                return Result.Failure(new ForbiddenError());

            // Multi-tenant isolation
            var tenantId = currentUser.TenantId
                ?? throw new InvalidOperationException("Tenant required");

            // Audit trail
            entity.CreatedBy = currentUser.Id;
            entity.UpdatedBy = currentUser.Id;

            // Impersonation awareness
            if (currentUser.Delegation is { } delegation)
                logger.LogWarning("Action by {Id} impersonated by {Impersonator}",
                    currentUser.Id, delegation.ActorId);
        """);
        Console.WriteLine();
    }
}
