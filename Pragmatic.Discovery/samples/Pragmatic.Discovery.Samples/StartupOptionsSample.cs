// Pragmatic.Discovery Samples - ValidateOnStartup / ThrowOnValidationFailure behavior.

using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.Models;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates how <see cref="DiscoveryOptions.ValidateOnStartup"/> and
/// <see cref="DiscoveryOptions.ThrowOnValidationFailure"/> govern startup behavior.
/// The startup gate is: validate → if invalid AND ThrowOnValidationFailure → throw; otherwise log.
/// </summary>
/// <remarks>
/// The built-in validator only escalates to Error severity in rare cases, so to make the
/// "throw" branch observable this sample seeds an explicit Error-severity result and applies the
/// same decision the hosted service makes.
/// </remarks>
internal static class StartupOptionsSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("ValidateOnStartup / ThrowOnValidationFailure");

        // ValidateOnStartup = false → no validation runs (registration only).
        SampleConsole.Step("ValidateOnStartup = false → validation skipped at startup.");
        await ShowValidate(validateOnStartup: false);

        // ValidateOnStartup = true → validation runs; warnings/info are logged, startup proceeds.
        SampleConsole.Step("ValidateOnStartup = true, ThrowOnValidationFailure = false → log only.");
        await ShowValidate(validateOnStartup: true);

        // ThrowOnValidationFailure semantics applied to an Error-severity result.
        SampleConsole.Step("ThrowOnValidationFailure governs whether an Error-severity result blocks startup:");
        var errorResult = new DiscoveryValidationResult
        {
            IsValid = false,
            Issues =
            [
                new DiscoveryValidationIssue
                {
                    Code = "DISCXXX",
                    Severity = IssueSeverity.Error,
                    Message = "Example: module owned by two hosts on incompatible databases.",
                },
            ],
        };

        ApplyStartupGate(errorResult, throwOnFailure: false);
        ApplyStartupGate(errorResult, throwOnFailure: true);

        SampleConsole.Blank();
    }

    private static async Task ShowValidate(bool validateOnStartup)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDiscovery(o =>
        {
            o.AutoRegisterOnStartup = false;
            o.ValidateOnStartup = validateOnStartup;
        });
        await using var provider = services.BuildServiceProvider();
        var discovery = provider.GetRequiredService<IDiscoveryService>();

        await discovery.RegisterAsync(SampleData.MainHost());
        if (validateOnStartup)
        {
            var result = await discovery.ValidateAsync(SampleData.MainHost());
            SampleConsole.Info($"  validate → IsValid={result.IsValid}, Issues={result.Issues.Count}");
        }
        else
        {
            SampleConsole.Info("  (no ValidateAsync call performed)");
        }
    }

    /// <summary>Replicates the hosted service's startup decision for a given result.</summary>
    private static void ApplyStartupGate(DiscoveryValidationResult result, bool throwOnFailure)
    {
        if (result.IsValid)
        {
            SampleConsole.Info($"  ThrowOnValidationFailure={throwOnFailure}: valid → startup continues.");
            return;
        }

        if (throwOnFailure)
        {
            try
            {
                var errors = string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"));
                throw new InvalidOperationException($"Discovery validation failed: {errors}");
            }
            catch (InvalidOperationException ex)
            {
                SampleConsole.Info($"  ThrowOnValidationFailure=true: startup BLOCKED → {ex.GetType().Name}.");
            }
        }
        else
        {
            SampleConsole.Info($"  ThrowOnValidationFailure=false: {result.Errors.Count()} error(s) logged, startup continues.");
        }
    }
}
