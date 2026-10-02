using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Management.Actions;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Management package <c>DomainAction</c>s — <see cref="SetConfigValue"/>,
///     <see cref="GetConfigValue"/>, and <see cref="GetConfigAuditLog"/> — exercised directly.
///     At runtime the source-generated action invoker injects the <c>_store</c> / <c>_auditStore</c>
///     dependencies and runs the validation/authorization pipeline; here we inject them by reflection
///     (as the module's own unit tests do) so the action bodies can be run in isolation.
/// </summary>
public static class ManagementActionsSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Management Actions — Set / Get / Audit DomainActions");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Back the actions with a real SQLite-backed store so audit entries are genuine.
        using var factory = new SqliteConnectionFactory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbConnectionFactory>(factory);
        services.AddDatabaseConfigurationStore(options =>
        {
            options.Provider = DatabaseProvider.Sqlite;
            options.AuditUser = "admin@pragmatic";
        });
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IConfigurationStore>();

        // ── SetConfigValue (command) ────────────────────────────────────────────
        // Only a declared setting is writable: the catalogue the generated host fills from the
        // [Configuration] sections is handed in by hand here, declaring App:Theme.
        var setAction = Declared(Inject(new SetConfigValue { Key = "App:Theme", Value = "dark" }, "_store", store));
        var setResult = await setAction.Execute();
        Console.WriteLine($"  SetConfigValue(App:Theme=dark): IsSuccess={setResult.IsSuccess}");

        await Declared(Inject(new SetConfigValue { Key = "App:Theme", Value = "light" }, "_store", store)).Execute(); // update

        var undeclared = await Declared(Inject(new SetConfigValue { Key = "App:Thme", Value = "dark" }, "_store", store)).Execute();
        Console.WriteLine($"  SetConfigValue(App:Thme): IsFailure={undeclared.IsFailure} — no section declares it");

        // ── GetConfigValue (query) ──────────────────────────────────────────────
        var getAction = Inject(new GetConfigValue { Key = "App:Theme" }, "_store", store);
        var getResult = await getAction.Execute();
        if (getResult.TryGetValue(out var value))
            Console.WriteLine($"  GetConfigValue(App:Theme): Key={value.Key}, Value={value.Value}");

        // ── Validation path: empty key returns a BadRequest failure ────────────
        var invalid = await Inject(new GetConfigValue { Key = "" }, "_store", store).Execute();
        Console.WriteLine($"  GetConfigValue(\"\"): IsFailure={invalid.IsFailure}, Error={invalid.Error.GetType().Name}");
        Console.WriteLine();

        // ── GetConfigAuditLog (query) ───────────────────────────────────────────
        // Reads the framework audit trail rather than a table this module owns, so it needs an
        // IAuditTrailReader injected. Left out of this sample to keep it free of a database: see
        // Pragmatic.Audit for the trail, and note that the previous value comes back as a hash.
        Console.WriteLine("  GetConfigAuditLog reads Pragmatic.Audit — see that module's README.");

        // ── Missing audit store surfaces a configuration error (not a silent empty) ──
        var noStore = await new GetConfigAuditLog().Execute();
        Console.WriteLine($"  GetConfigAuditLog with no audit store: IsFailure={noStore.IsFailure}");
        Console.WriteLine();
    }

    /// <summary>
    ///     Sets the private dependency field that the SG-generated invoker normally populates,
    ///     so the action body can be exercised standalone.
    /// </summary>
    /// <remarks>
    ///     Every management action also needs its caller: the tenant it belongs to decides which values it
    ///     may touch. This sample runs as a <see cref="PlatformOperator" />.
    /// </remarks>
    private static T Inject<T>(T action, string fieldName, object value)
    {
        Set(action, fieldName, value);
        Set(action, "_currentUser", PlatformOperator.Instance);
        return action;
    }

    private static SetConfigValue Declared(SetConfigValue action)
    {
        var catalog = new Pragmatic.Configuration.Discovery.ConfigurationCatalog();
        catalog.Contribute([
            new Pragmatic.Configuration.Discovery.ConfigurationSectionDescriptor
            {
                SectionPath = "App",
                TypeName = "Samples.AppOptions",
                Properties = [new Pragmatic.Configuration.Discovery.ConfigurationPropertyDescriptor { Name = "Theme", TypeName = "string" }],
            },
        ]);
        Set(action, "_catalog", catalog);
        return action;
    }

    private static void Set<T>(T action, string fieldName, object value)
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found on {typeof(T).Name}.");
        field.SetValue(action, value);
    }
}
