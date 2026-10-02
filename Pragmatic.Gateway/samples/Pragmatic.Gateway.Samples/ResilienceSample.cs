using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Gateway.Resilience;
using Pragmatic.Resilience.State;

namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Configures Gateway resilience (<see cref="GatewayResilienceOptions" /> /
///     <see cref="ClusterResiliencePolicy" />), wires it into DI via
///     <see cref="GatewayResilienceExtensions.AddGatewayResilience(IServiceCollection, GatewayResilienceOptions)" />,
///     and then drives a real circuit breaker open/half-open cycle against the public
///     <see cref="InMemoryCircuitBreakerStateStore" />.
/// </summary>
internal static class ResilienceSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("GatewayResilience — circuit breaker + timeout");

        var resilience = new GatewayResilienceOptions
        {
            Enabled = true,
            Default = new ClusterResiliencePolicy
            {
                Timeout = TimeSpan.FromSeconds(10),
                CircuitBreakerEnabled = true,
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromSeconds(30)
            },
            Clusters =
            {
                // Per-cluster override: a slow analytics backend gets a longer timeout + higher threshold.
                ["analytics"] = new ClusterResiliencePolicy
                {
                    Timeout = TimeSpan.FromSeconds(30),
                    FailureThreshold = 10,
                    BreakDuration = TimeSpan.FromMinutes(1)
                }
            }
        };

        SampleConsole.Section("Default policy");
        Describe(resilience.Default);
        SampleConsole.Section("Per-cluster override: 'analytics'");
        Describe(resilience.Clusters["analytics"]);

        // Range validation guard exposed on the policy.
        SampleConsole.Section("FailureStatusCode range validation");
        var bad = new ClusterResiliencePolicy { FailureStatusCodeMin = 599, FailureStatusCodeMax = 500 };
        try
        {
            bad.Validate();
            SampleConsole.Item("inverted range", "no error (unexpected)");
        }
        catch (InvalidOperationException ex)
        {
            SampleConsole.Item("inverted range", $"rejected: {ex.Message}");
        }

        // ── DI wiring via the public extension ───────────────────────────────────────────────
        SampleConsole.Section("DI wiring (AddGatewayResilience)");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayResilience(resilience);
        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ICircuitBreakerStateStore>();
        var registeredOptions = provider.GetRequiredService<GatewayResilienceOptions>();
        SampleConsole.Item("ICircuitBreakerStateStore", store.GetType().Name);
        SampleConsole.Item("GatewayResilienceOptions", registeredOptions.Enabled ? "registered (Enabled)" : "registered");

        // ── Drive the circuit breaker through its lifecycle ──────────────────────────────────
        SampleConsole.Section("Circuit breaker lifecycle (cluster 'api', threshold=3)");
        const string cluster = "api";
        var policy = resilience.Default;

        for (var attempt = 1; attempt <= policy.FailureThreshold; attempt++)
        {
            await store.RecordFailureAsync(cluster);
            var snap = await store.GetSnapshotAsync(cluster);

            // Same open-decision the ProxyResilienceMiddleware makes after recording a failure.
            if (snap.FailureCount >= policy.FailureThreshold && snap.State == CircuitState.Closed)
                await store.TransitionToAsync(cluster, CircuitState.Open, policy.BreakDuration);

            var after = await store.GetSnapshotAsync(cluster);
            SampleConsole.Item($"failure #{attempt}", $"count={after.FailureCount}, state={after.State}");
        }

        var open = await store.GetSnapshotAsync(cluster);
        SampleConsole.Item("circuit state", $"{open.State} (fast-rejects requests, returns 503 + Retry-After)");

        // Probe transition: once the break duration has elapsed, the next request is allowed as a probe.
        var afterBreak = DateTimeOffset.UtcNow + policy.BreakDuration + TimeSpan.FromSeconds(1);
        var transitioned = await store.TryTransitionToHalfOpenAsync(cluster, afterBreak);
        var halfOpen = await store.GetSnapshotAsync(cluster);
        SampleConsole.Item("after break elapsed", $"transitioned={transitioned}, state={halfOpen.State}");

        // A successful probe closes the circuit again.
        await store.RecordSuccessAsync(cluster);
        var recovered = await store.GetSnapshotAsync(cluster);
        SampleConsole.Item("probe succeeds", $"state={recovered.State}, count={recovered.FailureCount}");
    }

    private static void Describe(ClusterResiliencePolicy policy)
    {
        SampleConsole.Item("Timeout", policy.Timeout?.ToString() ?? "(none)");
        SampleConsole.Item("CircuitBreakerEnabled", policy.CircuitBreakerEnabled);
        SampleConsole.Item("FailureThreshold", policy.FailureThreshold);
        SampleConsole.Item("BreakDuration", policy.BreakDuration);
        SampleConsole.Item("Failure status range", $"{policy.FailureStatusCodeMin}-{policy.FailureStatusCodeMax}");
    }
}
