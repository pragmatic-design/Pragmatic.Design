using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Runnable ResourcePolicy evaluation against real ICurrentUser instances.
///     Shows how policies evaluate against different user contexts.
/// </summary>
public static class PolicyEvaluationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Policy Evaluation — Runnable Authorization Checks");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowBasicEvaluation();
        ShowComposedEvaluation();
        ShowPrincipalKindPolicies();

        Console.WriteLine();
    }

    private static void ShowBasicEvaluation()
    {
        Console.WriteLine("  4.1 Basic policy evaluation against user contexts");
        Console.WriteLine("  ---------------------------------------------------");

        var anon = AnonymousUser.Instance;
        var system = SystemUser.Instance;

        var mustBeAuth = ResourcePolicy.IsAuthenticated();
        Console.WriteLine($"    IsAuthenticated().Evaluate(AnonymousUser): {mustBeAuth.Evaluate(anon)}");
        Console.WriteLine($"    IsAuthenticated().Evaluate(SystemUser):    {mustBeAuth.Evaluate(system)}");

        var allowAll = ResourcePolicy.Allow;
        var denyAll = ResourcePolicy.Deny;
        Console.WriteLine($"    Allow.Evaluate(AnonymousUser):             {allowAll.Evaluate(anon)}");
        Console.WriteLine($"    Deny.Evaluate(SystemUser):                 {denyAll.Evaluate(system)}");
        Console.WriteLine();
    }

    private static void ShowComposedEvaluation()
    {
        Console.WriteLine("  4.2 Composed policies — AND, OR, NOT evaluation");
        Console.WriteLine("  ---------------------------------------------------");

        var anon = AnonymousUser.Instance;
        var system = SystemUser.Instance;

        // Service OR authenticated
        var serviceOrAuth = ResourcePolicy.HasPrincipalKind(PrincipalKind.System)
                            | ResourcePolicy.IsAuthenticated();

        Console.WriteLine($"    (System | Authenticated):");
        Console.WriteLine($"      AnonymousUser: {serviceOrAuth.Evaluate(anon)}");
        Console.WriteLine($"      SystemUser:    {serviceOrAuth.Evaluate(system)}");

        // NOT anonymous
        var notAnon = !ResourcePolicy.HasPrincipalKind(PrincipalKind.Anonymous);
        Console.WriteLine($"    !Anonymous:");
        Console.WriteLine($"      AnonymousUser: {notAnon.Evaluate(anon)}");
        Console.WriteLine($"      SystemUser:    {notAnon.Evaluate(system)}");

        // Complex: system AND authenticated (always true for SystemUser)
        var systemAndAuth = ResourcePolicy.HasPrincipalKind(PrincipalKind.System)
                            & ResourcePolicy.IsAuthenticated();
        Console.WriteLine($"    (System & Authenticated):");
        Console.WriteLine($"      SystemUser:    {systemAndAuth.Evaluate(system)}");
        Console.WriteLine($"      AnonymousUser: {systemAndAuth.Evaluate(anon)}");
        Console.WriteLine();
    }

    private static void ShowPrincipalKindPolicies()
    {
        Console.WriteLine("  4.3 PrincipalKind-based policies");
        Console.WriteLine("  -----------------------------------");

        var anon = AnonymousUser.Instance;
        var system = SystemUser.Instance;

        var kinds = new[] { PrincipalKind.Anonymous, PrincipalKind.User, PrincipalKind.Service, PrincipalKind.System };

        Console.WriteLine($"    {"Policy",-30} {"AnonymousUser",-15} {"SystemUser",-15}");
        Console.WriteLine($"    {"──────",-30} {"─────────────",-15} {"──────────",-15}");

        foreach (var kind in kinds)
        {
            var policy = ResourcePolicy.HasPrincipalKind(kind);
            Console.WriteLine($"    HasPrincipalKind({kind,-10})    {policy.Evaluate(anon),-15} {policy.Evaluate(system),-15}");
        }

        Console.WriteLine();
    }
}
