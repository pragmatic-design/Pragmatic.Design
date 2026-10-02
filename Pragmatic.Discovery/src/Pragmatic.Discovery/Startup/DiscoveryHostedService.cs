// Pragmatic.Discovery - Discovery Hosted Service

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Metadata;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Models;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Startup;

/// <summary>
/// Background service that auto-registers the current host's topology on startup.
/// Reads the <c>[assembly: PragmaticMetadata(HostTopology, ...)]</c> attribute
/// from the entry assembly and registers it with <see cref="IDiscoveryService"/>.
/// </summary>
internal sealed class DiscoveryHostedService(
    IDiscoveryService discoveryService,
    IOptions<DiscoveryOptions> options,
    IHostEnvironment environment,
    ILogger<DiscoveryHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;

        // Warn operators running in a non-development environment with ThrowOnValidationFailure disabled.
        if (!opts.ThrowOnValidationFailure && !environment.IsDevelopment())
            logger.LogWarning(
                "DiscoveryOptions.ThrowOnValidationFailure is false in '{EnvironmentName}'. " +
                "Topology validation errors will be logged but will not prevent startup. " +
                "Consider setting ThrowOnValidationFailure = true in staging/production.",
                environment.EnvironmentName);

        if (!opts.AutoRegisterOnStartup)
        {
            logger.LogDebug("Discovery auto-registration is disabled. Skipping.");
            return;
        }

        // Refuse an incompatible schema major explicitly (clear diagnostic), instead of returning a
        // silent null that reads as "no topology found" and mis-triggers the reflection fallback.
        var topologyEntry = AssemblyMetadataRegistry.FindByCategory(MetadataCategory.HostTopology);
        if (topologyEntry.HasValue && !HostTopologyInfo.IsCompatibleVersion(topologyEntry.Value.SchemaVersion))
        {
            logger.LogError(
                "HostTopology metadata schema version '{Version}' is incompatible with this build "
                + "(expected major {Major}). The host and its referenced Pragmatic packages are out of sync — "
                + "align their versions. Discovery registration skipped.",
                topologyEntry.Value.SchemaVersion, HostTopologyInfo.SchemaMajor);
            return;
        }

        // Prefer zero-reflection registry path; log a warning if the fallback to reflection is used.
        var topology = HostTopologyInfo.FromRegistry();
        if (topology is null)
        {
            // Registry miss — fall back to assembly attribute reflection (one-time startup cost).
            logger.LogWarning(
                "AssemblyMetadataRegistry has no HostTopology entry. Falling back to assembly attribute reflection. " +
                "For AOT-safe startup, ensure the host project's SG emits AssemblyMetadataRegistry registration.");
            topology = HostTopologyInfo.FromEntryAssembly();
        }

        if (topology is null)
        {
            logger.LogWarning(
                "No HostTopology metadata found in entry assembly. " +
                "Ensure the host project references Pragmatic.Composition and has a [Module] class with [Include<T>] attributes. " +
                "Discovery registration skipped.");
            return;
        }

        // Discovery is an auxiliary subsystem (topology observability). A backend I/O failure
        // (Redis/Consul unreachable, etc.) must NOT abort the whole host boot — without this guard the
        // exception propagates out of IHostedService.StartAsync and ASP.NET Core aborts startup.
        try
        {
            await discoveryService.RegisterAsync(topology, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Discovery registration failed for host '{HostName}' (backend unavailable). " +
                "The host will start; topology registration/validation is skipped this run.",
                topology.HostName);
            return;
        }

        if (!opts.ValidateOnStartup)
            return;

        DiscoveryValidationResult result;
        try
        {
            result = await discoveryService.ValidateAsync(topology, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Discovery validation could not run for host '{HostName}' (backend unavailable). The host will start.",
                topology.HostName);
            return;
        }

        if (result.IsValid)
            return;

        if (opts.ThrowOnValidationFailure)
        {
            var errors = string.Join(Environment.NewLine, result.Errors.Select(e => $"  [{e.Code}] {e.Message}"));
            throw new InvalidOperationException(
                $"Discovery validation failed for host '{topology.HostName}':{Environment.NewLine}{errors}");
        }

        // ThrowOnValidationFailure is false — log each issue so operators are not left in the dark.
        foreach (var issue in result.Issues)
        {
            switch (issue.Severity)
            {
                case IssueSeverity.Error:
                    logger.LogError("[{Code}] {Message}", issue.Code, issue.Message);
                    break;
                case IssueSeverity.Warning:
                    logger.LogWarning("[{Code}] {Message}", issue.Code, issue.Message);
                    break;
                default:
                    logger.LogInformation("[{Code}] {Message}", issue.Code, issue.Message);
                    break;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
