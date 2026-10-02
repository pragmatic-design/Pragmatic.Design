using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.EFCore.Samples.Filtering;

/// <summary>
///     Demonstrates programmatic control of the query-filter pipeline via
///     <see cref="IQueryFilterToggle"/> and <see cref="FilterMode"/>. The toggle uses
///     <c>AsyncLocal</c> scopes: filters are disabled only inside the <c>using</c> block and
///     re-enabled automatically on dispose. The repository reads the current mode / disabled set
///     when composing the FilterMap.
/// </summary>
public static class FilterToggleSample
{
    public static void Run()
    {
        Console.WriteLine("═══ Filter Mode + IQueryFilterToggle ═══");
        Console.WriteLine();

        IQueryFilterToggle toggle = new QueryFilterToggle();

        Console.WriteLine($"  Default mode               : {toggle.CurrentMode} (expects Normal)");
        Console.WriteLine($"  SoftDelete disabled?       : {toggle.IsDisabled<SampleSoftDeleteFilter>()} (expects False)");
        Console.WriteLine();

        // ── Scoped disable of a single filter ──
        Console.WriteLine("  Entering scope: Disable<SampleSoftDeleteFilter>()");
        using (toggle.Disable<SampleSoftDeleteFilter>())
        {
            Console.WriteLine($"    inside  → SoftDelete disabled? {toggle.IsDisabled<SampleSoftDeleteFilter>()} (expects True)");

            // ── Nested: switch to Admin mode for the duration of an admin operation ──
            using (toggle.UseMode(FilterMode.Admin))
            {
                Console.WriteLine($"    nested  → mode {toggle.CurrentMode}, disabled set has {toggle.GetDisabledFilterTypes().Count} type(s)");
            }
            Console.WriteLine($"    after nested → mode reverted to {toggle.CurrentMode}");
        }
        Console.WriteLine($"  After scope → SoftDelete disabled? {toggle.IsDisabled<SampleSoftDeleteFilter>()} (expects False)");
        Console.WriteLine();

        // ── DisableAll for a raw maintenance pass ──
        using (toggle.DisableAll())
        {
            Console.WriteLine($"  DisableAll() → AllDisabled = {toggle.AllDisabled} (expects True)");
        }
        Console.WriteLine($"  After DisableAll scope → AllDisabled = {toggle.AllDisabled} (expects False)");
        Console.WriteLine();

        Console.WriteLine("  FilterMode meanings:");
        foreach (var mode in Enum.GetValues<FilterMode>())
            Console.WriteLine($"    {mode,-10} — {Describe(mode)}");
        Console.WriteLine();
    }

    private static string Describe(FilterMode mode) => mode switch
    {
        FilterMode.Normal => "all filters active (authenticated user requests)",
        FilterMode.Admin => "skip visibility/permission, keep SoftDelete + Tenant",
        FilterMode.Background => "skip tenant too; background jobs, keep SoftDelete",
        FilterMode.Raw => "no automatic filters at all (use with care)",
        _ => ""
    };
}
