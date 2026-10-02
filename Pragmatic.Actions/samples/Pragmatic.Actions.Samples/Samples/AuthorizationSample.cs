using Pragmatic.Actions.Pipeline;
using Pragmatic.Actions.Samples.Actions;
using Pragmatic.Actions.Samples.Authorization;
using Pragmatic.Actions.Samples.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     Authorization &amp; filter pipeline. Demonstrates (and actually executes) the real APIs:
///     <c>[RequirePermission]</c> / <c>[RequireAnyPermission]</c> on actions, the
///     <c>[RequirePolicy&lt;TPolicy&gt;]</c> attribute + <see cref="ResourcePolicy" /> evaluation,
///     <see cref="Pragmatic.Authorization.IResourceAuthorizer{T}" /> invocation,
///     <see cref="ActionCallContext.IsInternalCall" /> bypass, and a custom <see cref="IActionFilter" />.
/// </summary>
public static class AuthorizationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Authorization & Filters — Permissions, Policies, Internal Call");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowDeclarativeAuthorization();
        await ShowResourceAuthorizerAsync();
        ShowInternalCallBypass();
        await ShowCustomFilterAsync();

        Console.WriteLine();
    }

    private static void ShowDeclarativeAuthorization()
    {
        Console.WriteLine("  8.1 Declarative authorization attributes on runnable actions");
        Console.WriteLine("  ---------------------------------------------------------------");

        // Read the real attributes off the compiled action types (live, applied attributes).
        var require = (RequirePermissionAttribute?)Attribute.GetCustomAttribute(
            typeof(CreateOrderRestrictedAction), typeof(RequirePermissionAttribute));
        var requireAny = (RequireAnyPermissionAttribute?)Attribute.GetCustomAttribute(
            typeof(ApproveOrderAction), typeof(RequireAnyPermissionAttribute));

        Console.WriteLine($"    CreateOrderRestrictedAction [RequirePermission]    = [{string.Join(", ", require?.Permissions ?? [])}]");
        Console.WriteLine($"    ApproveOrderAction          [RequireAnyPermission] = [{string.Join(", ", requireAny?.Permissions ?? [])}]");
        Console.WriteLine();
        Console.WriteLine("    Enforced at runtime (Order 200) by PermissionAuthorizationFilter");
        Console.WriteLine("    before the action's Execute() runs.");
        Console.WriteLine();

        // [RequirePolicy<TPolicy>] — shown as the real attribute + policy type (not applied to a
        // [DomainAction] here to avoid a known SG hint collision; see BUGS FOUND).
        var policyAttr = typeof(RequirePolicyAttribute<CanManageOrdersPolicy>);
        var policy = new CanManageOrdersPolicy();
        Console.WriteLine($"    [RequirePolicy<CanManageOrdersPolicy>] — attribute type : {policyAttr.Name}");
        Console.WriteLine($"      Policy evaluates (anonymous user) = {policy.Evaluate(AnonymousUser.Instance)}");
        Console.WriteLine($"      Policy evaluates (system user)    = {policy.Evaluate(SystemUser.Instance)}");
        Console.WriteLine("      Evaluated at runtime (Order 210) by PolicyEvaluationFilter.");
        Console.WriteLine();
    }

    private static async Task ShowResourceAuthorizerAsync()
    {
        Console.WriteLine("  8.2 IResourceAuthorizer<T> — instance-level authorization");
        Console.WriteLine("  ------------------------------------------------------------");

        var authorizer = new ApproveOrderAuthorizer();
        var action = new ApproveOrderAction { OrderId = Guid.NewGuid() };

        // Anonymous → denied (example rule). Authenticated would be allowed.
        var anonymousAllowed = await authorizer.CanAccessAsync(
            AnonymousUser.Instance, action, nameof(ApproveOrderAction), CancellationToken.None);

        Console.WriteLine($"    Anonymous user can approve order? {anonymousAllowed}");
        Console.WriteLine("    // Registered via AddResourceAuthorizer<ApproveOrderAuthorizer, ApproveOrderAction>();");
        Console.WriteLine("    // ResourceAuthorizationFilter (Order 250) calls CanAccessAsync per invocation.");
        Console.WriteLine();
    }

    private static void ShowInternalCallBypass()
    {
        Console.WriteLine("  8.3 ActionCallContext.IsInternalCall — authorization bypass");
        Console.WriteLine("  --------------------------------------------------------------");

        // Real scoped context as registered in DI. Authorization filters skip checks while
        // IsInternalCall is true (one action calling another within the same boundary, or a
        // system event reaction).
        var context = new ActionCallContext();

        Console.WriteLine($"    External call         : IsInternalCall = {context.IsInternalCall}  → checks enforced");

        using (context.EnterInternalCall())
        {
            Console.WriteLine($"    Inside internal scope : IsInternalCall = {context.IsInternalCall}  → checks bypassed");

            using (context.EnterInternalCall())
                Console.WriteLine($"      Nested scope        : IsInternalCall = {context.IsInternalCall}  (depth-counted)");

            Console.WriteLine($"    After nested dispose  : IsInternalCall = {context.IsInternalCall}  (still inside outer)");
        }

        Console.WriteLine($"    After outer dispose   : IsInternalCall = {context.IsInternalCall}  → checks enforced again");
        Console.WriteLine();
    }

    private static async Task ShowCustomFilterAsync()
    {
        Console.WriteLine("  8.4 Custom IActionFilter — global cross-cutting filter");
        Console.WriteLine("  ---------------------------------------------------------");

        var trail = new List<string>();
        var filter = new AuditActionFilter(trail);

        Console.WriteLine($"    AuditActionFilter.Order = {filter.Order}  (runs just before built-in logging at 1000)");
        Console.WriteLine();

        // Drive the filter directly the way the invoker pipeline would: BeforeExecute, then
        // (after the action) AfterExecute. This is the exact contract the pipeline invokes.
        var action = new ComputeTotalAction { Prices = [10m, 5m], Quantities = [2, 3] };

        var before = await filter.BeforeExecuteAsync<ComputeTotalAction, decimal>(action, CancellationToken.None);
        Console.WriteLine($"    BeforeExecute returned success = {before.IsSuccess} (failure would short-circuit)");

        var result = await action.Execute();
        await filter.AfterExecuteAsync(action, result, CancellationToken.None);

        foreach (var entry in trail)
            Console.WriteLine($"      {entry}");

        Console.WriteLine();
        Console.WriteLine("    Registration (global): services.TryAddEnumerable(");
        Console.WriteLine("        ServiceDescriptor.Singleton<IActionFilter, AuditActionFilter>());");
        Console.WriteLine("    Action-specific: implement IActionFilter<TAction> and register it scoped.");
        Console.WriteLine();
    }
}
