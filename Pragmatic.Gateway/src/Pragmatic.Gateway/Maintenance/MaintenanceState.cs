using System.Text.Json;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Gateway.Maintenance;

/// <summary>
///     Tracks which apps are in maintenance mode.
///     Listens for Agent KV changes on <c>state/app:*</c> to detect maintenance transitions.
///     All mutable state is protected by a single lock so concurrent KV notifications are safe.
/// </summary>
/// <remarks>
///     The roster has one entry per running instance, <c>state/app:{appId}/{instanceId}</c>. An app
///     is in maintenance when every instance of it is: one instance draining or in maintenance leaves the app
///     served by the others, which is the point of running two.
/// </remarks>
internal sealed class MaintenanceState
{
    // Per roster entry: the app it belongs to (a deletion carries no descriptor to read it from) and whether
    // that instance is unavailable.
    private readonly Dictionary<string, (string AppId, bool Unavailable)> _instances = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private bool _isFullMaintenance;

    /// <summary>Whether any app is currently in maintenance.</summary>
    public bool IsAnyInMaintenance
    {
        get
        {
            lock (_lock)
                return _instances.Values.Select(instance => instance.AppId).Distinct(StringComparer.Ordinal)
                    .Any(AllInstancesUnavailable);
        }
    }

    /// <summary>Whether a specific app is in maintenance: it is listed, and every instance of it is unavailable.</summary>
    public bool IsInMaintenance(string appId)
    {
        lock (_lock) return AllInstancesUnavailable(appId);
    }

    // Called under _lock.
    private bool AllInstancesUnavailable(string appId)
    {
        var any = false;
        foreach (var (instanceAppId, unavailable) in _instances.Values)
        {
            if (!string.Equals(instanceAppId, appId, StringComparison.Ordinal))
                continue;
            if (!unavailable)
                return false;
            any = true;
        }

        return any;
    }

    /// <summary>Whether ALL apps are in maintenance (full gateway maintenance).</summary>
    public bool IsFullMaintenance
    {
        get { lock (_lock) return _isFullMaintenance; }
        private set { lock (_lock) _isFullMaintenance = value; }
    }

    /// <summary>Subscribe to Agent KV changes for state tracking.</summary>
    public void SubscribeToAgent(AgentConnection agent)
    {
        agent.OnKvChanged += OnKvChanged;
    }

    private void OnKvChanged(KvChangedPayload change)
    {
        // Track app state changes. Since the P2 host-lifecycle work, the state/app: value is a JSON
        // HostDescriptorPayload (not a bare state string), so we read the descriptor's State field. A
        // deleted key (host gone) or an unparseable value clears the app from the maintenance set.
        if (change.Key.StartsWith(GatewayKvSchema.HostRosterPrefix, StringComparison.Ordinal))
        {
            lock (_lock)
            {
                if (change.Deleted)
                {
                    _instances.Remove(change.Key);
                }
                else if (ReadDescriptor(change.Value) is { } descriptor)
                {
                    _instances[change.Key] = (descriptor.AppId, IsUnavailableState(descriptor.State));
                }
                else
                {
                    // Unreadable: the instance cannot be told to be unavailable, so it does not hold its app
                    // in maintenance.
                    _instances.Remove(change.Key);
                }
            }
        }

        // Track global maintenance flag — use the same lock so IsFullMaintenance and
        // _appsInMaintenance are always consistent from the caller's perspective. This key is the
        // explicit full-gateway toggle, set out-of-band by an operator (`pragmatic maintenance on|off`),
        // distinct from the per-app state/app: descriptors above.
        if (change.Key == GatewayKvSchema.FullMaintenanceKey)
        {
            lock (_lock)
            {
                _isFullMaintenance = change.Value == "true" && !change.Deleted;
            }
        }
    }

    /// <summary>The <see cref="HostDescriptorPayload" /> a <c>state/app:</c> value holds, or null when it holds none.</summary>
    /// <remarks>
    ///     A bare state string is not read: it names no app, and the key does not either.
    /// </remarks>
    private static HostDescriptorPayload? ReadDescriptor(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '{')
            return null;

        try
        {
            return JsonSerializer.Deserialize<HostDescriptorPayload>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUnavailableState(string? state) => state switch
    {
        null => false,
        _ => state.Equals("Maintenance", StringComparison.OrdinalIgnoreCase)
             || state.Equals("Draining", StringComparison.OrdinalIgnoreCase)
             || state.Equals("Drained", StringComparison.OrdinalIgnoreCase)             || state.Equals("Migrating", StringComparison.OrdinalIgnoreCase)
    };
}
