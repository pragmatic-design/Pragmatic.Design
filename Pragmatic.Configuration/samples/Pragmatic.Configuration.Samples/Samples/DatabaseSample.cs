using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Database-backed <see cref="IConfigurationStore"/> over SQLite via
///     <see cref="DatabaseConfigurationExtensions.AddDatabaseConfigurationStore"/>: the schema is
///     auto-created on first use, values round-trip through real SQL, and every write is recorded in
/// </summary>
/// <remarks>
///     Uses an in-memory SQLite database (<see cref="SqliteConnectionFactory"/>) so the sample is
///     fully self-contained. In production register a provider-specific
///     <see cref="IDbConnectionFactory"/> (Npgsql / Microsoft.Data.SqlClient) and set
///     <see cref="DatabaseConfigurationOptions.Provider"/> and <c>ConnectionString</c> accordingly.
/// </remarks>
public static class DatabaseSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Database Backend — SQLite store + schema auto-create + audit");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        using var factory = new SqliteConnectionFactory();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbConnectionFactory>(factory);
        services.AddDatabaseConfigurationStore(options =>
        {
            options.Provider = DatabaseProvider.Sqlite;
            options.AutoCreateSchema = true;       // tables created lazily on first call
            options.AuditUser = "sample-admin";    // recorded as changed_by in the audit log
        });

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IConfigurationStore>();

        // ── Writes (schema is created transparently on the first one) ──────────
        Console.WriteLine("  Writing configuration to SQLite:");
        await store.SetAsync("App:ConnectionTimeout", "30");
        await store.SetAsync("App:ConnectionTimeout", "60");          // update (version bump + audit)
        await store.SetAsync("App:MaxConnections", "100");
        await store.SetAsync("App:Region", "eu-west", tenantId: "tenant-1");

        Console.WriteLine($"    App:ConnectionTimeout      = {await store.GetAsync("App:ConnectionTimeout")}");
        Console.WriteLine($"    App:MaxConnections         = {await store.GetAsync("App:MaxConnections")}");
        Console.WriteLine($"    App:Region (tenant-1)      = {await store.GetAsync("App:Region", "tenant-1")}");
        Console.WriteLine();

        // ── Section query ───────────────────────────────────────────────────────
        var section = await store.GetSectionAsync("App:");
        Console.WriteLine($"  Section 'App:' ({section.Count} keys):");
        foreach (var (key, value) in section.OrderBy(kv => kv.Key))
            Console.WriteLine($"    {key} = {value}");
        Console.WriteLine();

        // ── Delete ──────────────────────────────────────────────────────────────
        await store.DeleteAsync("App:MaxConnections");
        Console.WriteLine($"  After delete: App:MaxConnections = {await store.GetAsync("App:MaxConnections") ?? "(null)"}");
        Console.WriteLine();

        // ── Audit ──────────────────────────────────────────────────────────────
        // Every Set and Delete above also wrote to the framework audit trail, inside the same
        // transaction as the change. Reading it back is Pragmatic.Audit's job now, not this module's:
        // see IAuditTrailReader, or the GetConfigAuditLog management action.
        Console.WriteLine("  Each change above was recorded on the audit trail (Pragmatic.Audit),");
        Console.WriteLine("  in the same transaction, with a hash of the previous value and never the value.");
        Console.WriteLine();
    }
}
