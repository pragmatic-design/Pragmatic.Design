using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Configuration;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Configuration.Management.Actions;

/// <summary>
///     Query: retrieves a single configuration value by key.
/// </summary>
[DomainAction]
[RequirePermission("configuration.values.read")]
[BelongsTo<ConfigurationManagementPackage>]
public sealed partial class GetConfigValue : DomainAction<ConfigValueResult>
{
    private IConfigurationStore _store = null!;
    private ICurrentUser _currentUser = null!;

    /// <summary>Configuration key (e.g., "App:Database:Host").</summary>
    public required string Key { get; set; }

    /// <summary>Optional tenant filter.</summary>
    public string? TenantId { get; set; }

    public override async Task<Result<ConfigValueResult, IError>> Execute(CancellationToken ct = default)
    {
        ThrowIfNull(_store, nameof(_store));
        ThrowIfNull(_currentUser, nameof(_currentUser));

        // The base value is what the caller's tenant inherits anyway: reading it discloses nothing.
        if (!string.IsNullOrEmpty(TenantId) && !TenantBinding.Permits(_currentUser, TenantId))
            return ForbiddenError.ActionDenied("get-config-value", $"tenant:{TenantId}");


        if (string.IsNullOrWhiteSpace(Key))
            return BadRequestError.Create("KeyRequired");

        var value = string.IsNullOrEmpty(TenantId)
            ? await _store.GetAsync(Key, ct).ConfigureAwait(false)
            : await _store.GetAsync(Key, TenantId, ct).ConfigureAwait(false);

        return new ConfigValueResult(Key, value, TenantId);
    }
}
