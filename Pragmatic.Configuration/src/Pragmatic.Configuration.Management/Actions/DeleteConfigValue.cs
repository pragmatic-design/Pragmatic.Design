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
///     Command: deletes a configuration value.
/// </summary>
[DomainAction]
[RequirePermission("configuration.values.delete")]
[BelongsTo<ConfigurationManagementPackage>]
public sealed partial class DeleteConfigValue : VoidDomainAction
{
    private IConfigurationStore _store = null!;
    private ICurrentUser _currentUser = null!;

    /// <summary>Configuration key to delete.</summary>
    public required string Key { get; set; }

    /// <summary>Optional tenant scope. Null = delete base value.</summary>
    public string? TenantId { get; set; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        ThrowIfNull(_store, nameof(_store));
        ThrowIfNull(_currentUser, nameof(_currentUser));

        if (!TenantBinding.Permits(_currentUser, TenantId))
            return VoidResult<IError>.Failure(ForbiddenError.ActionDenied("delete-config-value", $"tenant:{TenantId ?? "base"}"));


        if (string.IsNullOrWhiteSpace(Key))
            return VoidResult<IError>.Failure(BadRequestError.Create("KeyRequired"));

        await _store.DeleteAsync(Key, TenantId, ct).ConfigureAwait(false);
        return VoidResult<IError>.Success();
    }
}
