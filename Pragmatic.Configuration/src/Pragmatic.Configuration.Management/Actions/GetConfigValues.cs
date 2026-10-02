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
///     Query: retrieves configuration values by section prefix.
/// </summary>
[DomainAction]
[RequirePermission("configuration.values.read")]
[BelongsTo<ConfigurationManagementPackage>]
public sealed partial class GetConfigValues : DomainAction<Dictionary<string, string>>
{
    /// <summary>Upper bound on the number of entries returned, guarding against unbounded dumps.</summary>
    private const int MaxLimit = 1000;

    private IConfigurationStore _store = null!;
    private ICurrentUser _currentUser = null!;

    /// <summary>Section prefix (e.g., "App:Database"). Empty returns all (capped by <see cref="Limit"/>).</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Optional tenant filter.</summary>
    public string? TenantId { get; set; }

    /// <summary>Max entries to return (1..1000). Default: 500. Bounds an empty-prefix full dump.</summary>
    public int Limit { get; set; } = 500;

    public override async Task<Result<Dictionary<string, string>, IError>> Execute(CancellationToken ct = default)
    {
        ThrowIfNull(_store, nameof(_store));
        ThrowIfNull(_currentUser, nameof(_currentUser));

        // The base values are what the caller's tenant inherits anyway: reading them discloses nothing.
        if (!string.IsNullOrEmpty(TenantId) && !TenantBinding.Permits(_currentUser, TenantId))
            return ForbiddenError.ActionDenied("get-config-values", $"tenant:{TenantId}");


        var result = string.IsNullOrEmpty(TenantId)
            ? await _store.GetSectionAsync(Prefix, ct).ConfigureAwait(false)
            : await _store.GetSectionAsync(Prefix, TenantId, ct).ConfigureAwait(false);

        var cap = Math.Clamp(Limit, 1, MaxLimit);
        var capped = new Dictionary<string, string>(Math.Min(result.Count, cap));
        foreach (var kv in result)
        {
            if (capped.Count >= cap) break;
            capped[kv.Key] = kv.Value;
        }

        return capped;
    }
}
