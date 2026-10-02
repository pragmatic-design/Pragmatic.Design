using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Authorization;
using Pragmatic.Configuration.Resolution;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Configuration.Management.Actions;

/// <summary>
///     Query: resolves a configuration key through the full cascade (user → tenant → environment → base) and
///     reports <b>which</b> layer supplied the effective value — the operator-facing "why is this value what
///     it is?" answer.
/// </summary>
[DomainAction]
[RequirePermission("configuration.values.read")]
[BelongsTo<ConfigurationManagementPackage>]
public sealed partial class GetConfigResolution : DomainAction<ConfigResolutionResult>
{
    private IConfigurationResolver _resolver = null!;

    /// <summary>Configuration key to resolve (e.g., "App:Database:Host").</summary>
    public required string Key { get; set; }

    public override async Task<Result<ConfigResolutionResult, IError>> Execute(CancellationToken ct = default)
    {
        ThrowIfNull(_resolver, nameof(_resolver));

        if (string.IsNullOrWhiteSpace(Key))
            return BadRequestError.Create("KeyRequired");

        var resolved = await _resolver.ResolveWithTraceAsync(Key, ct).ConfigureAwait(false);

        return ConfigResolutionResult.From(Key, resolved);
    }
}
