using Pragmatic.Configuration.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Configuration Samples                  ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Core store + resolution (runnable, no external infra) ────────────────────

// 1. IConfigurationStore: real get/set/delete, section reads, multi-tenant overrides
await InMemoryStoreSample.RunAsync();

// 2. Hot-reload: WatchAsync change stream
await HotReloadSample.RunAsync();

// 3. Bridge: AddPragmaticStore() → IConfiguration + IOptionsMonitor<T> hot-reload
await BridgeSample.RunAsync();

// 4. Cascade resolution: EnvironmentProfile chain + ConfigurationResolver (tenant→env→base)
await CascadeResolutionSample.RunAsync();

// ── Backends (database runnable via SQLite; Azure is setup-only) ─────────────

// 5. Database backend: SQLite store + schema auto-create + audit log
await DatabaseSample.RunAsync();

// 6. Secrets at rest: AES-256-GCM database secret store
await EncryptionSample.RunAsync();

// 7. Azure App Configuration + Key Vault (registration/usage shape; needs live Azure to call)
AzureSample.Run();

// ── Management + caching ─────────────────────────────────────────────────────

// 8. Management package DomainActions: Set / Get / GetAuditLog
await ManagementActionsSample.RunAsync();

// 9. Read-through configuration caching via ICacheStack
await CacheStackSample.RunAsync();

// ── [Configuration] attribute — SG-driven options binding (reference) ─────────

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("10. [Configuration] Attribute — SG-Driven Options Binding");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("""
    // Mark a class with [Configuration] → the unified source generator emits:
    //   1. IOptions<T> binding from appsettings.json
    //   2. Startup validation (when ValidateOnStart = true)
    //   3. DI registration in the generated Add{Prefix}Configuration(), which the generated host calls

    [Configuration("Smtp")]
    public class SmtpOptions
    {
        [Required] public required string Host { get; init; }
        [Range(1, 65535)] public int Port { get; init; } = 587;
        public bool UseSsl { get; init; } = true;
    }

    // appsettings.json:  { "Smtp": { "Host": "mail.example.com", "Port": 587 } }

    // Consume via DI:
    public class EmailService(IOptions<SmtpOptions> options)
        => _host = options.Value.Host;   // "mail.example.com"

    // This binding requires the source generator + host build pipeline, so it is shown
    // as a reference rather than executed in this standalone console sample.
""");
Console.WriteLine();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
