using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     The fluent <see cref="PragmaticLoggingBuilder"/> exposes opinionated presets for whole
///     deployment profiles. This sample shows the compliance profile —
///     <c>UseCompliancePreset(GDPR)</c> combined with <c>EnableDataRedaction</c> and
///     <c>EnableAuditTrail</c> — and the throughput profile <c>UseHighPerformancePreset</c>.
///     Presets only configure <c>PragmaticLoggingOptions</c>; we resolve the options back out
///     and print the resulting profile so the effect is visible without external infrastructure.
/// </summary>
public static class CompliancePresetSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Compliance & high-performance presets ---");

        var services = new ServiceCollection();
        services.AddPragmaticLogging(pragmatic => pragmatic
            // GDPR profile: turns on redaction + secret detection + tamper-resistant audit.
            .UseCompliancePreset(ComplianceStandard.Gdpr)
            .EnableDataRedaction(redaction =>
            {
                redaction.RedactionPlaceholder = "[REDACTED]";
                redaction.PreserveLengths = false; // GDPR: complete redaction
                redaction.SensitivePropertyNames = ["email", "ssn", "creditCard"];
            })
            .EnableAuditTrail(audit =>
            {
                audit.BatchSize = 50;
                audit.EnableCompression = true;
                audit.StorageType = "Memory"; // self-contained sample (no external DB)
            })
            .AddConsole());

        using var provider = services.BuildServiceProvider();

        // Resolve the configured options to show what the presets produced.
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.PragmaticLoggingOptions>>()
            .Value;

        Console.WriteLine("GDPR compliance profile:");
        Console.WriteLine($"  Privacy.EnableRedaction       = {options.Privacy.EnableRedaction}");
        Console.WriteLine($"  Privacy.EnableSecretDetection = {options.Privacy.EnableSecretDetection}");
        Console.WriteLine($"  Privacy.ComplianceStandard    = {options.Privacy.ComplianceStandard}");
        Console.WriteLine($"  Privacy.Redaction.Placeholder = {options.Privacy.DataRedaction.RedactionPlaceholder}");
        Console.WriteLine($"  Audit.Enabled                 = {options.Audit.Enabled}");
        Console.WriteLine($"  Audit.BatchSize               = {options.Audit.BatchSize}");
        Console.WriteLine($"  Audit.StorageType             = {options.Audit.StorageType}");

        // The high-performance profile is the opposite trade-off: throughput over verbosity.
        var perfServices = new ServiceCollection();
        perfServices.AddPragmaticLogging(p => p.UseHighPerformancePreset().AddConsole());
        using var perfProvider = perfServices.BuildServiceProvider();
        var perfOptions = perfProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.PragmaticLoggingOptions>>()
            .Value;

        Console.WriteLine("\nHigh-performance profile:");
        Console.WriteLine($"  HighPerformanceMode               = {perfOptions.HighPerformanceMode}");
        Console.WriteLine($"  Performance.EnableZeroAllocation  = {perfOptions.Performance.EnableZeroAllocation}");
        Console.WriteLine($"  Performance.UseBackgroundProcessing = {perfOptions.Performance.UseBackgroundProcessing}");
        Console.WriteLine($"  Performance.BufferSize            = {perfOptions.Performance.BufferSize}");
        Console.WriteLine($"  MinimumLevel                      = {perfOptions.MinimumLevel}");
    }
}
