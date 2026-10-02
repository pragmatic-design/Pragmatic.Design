using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Gateway.Maintenance;
using Xunit;

namespace Pragmatic.Gateway.Tests.Maintenance;

public class MaintenanceStateTests
{
    private readonly MaintenanceState _state = new();

    [Fact]
    public void Initially_NotInMaintenance()
    {
        _state.IsAnyInMaintenance.Should().BeFalse();
        _state.IsFullMaintenance.Should().BeFalse();
    }

    [Fact]
    public void AppStateMaintenance_TriggersMaintenanceFlag()
    {
        SimulateKvChange(HostRosterKeys.Key("booking", "i-1"), Descriptor("booking", "i-1", "Maintenance"));

        _state.IsAnyInMaintenance.Should().BeTrue();
        _state.IsInMaintenance("booking").Should().BeTrue();
    }

    [Fact]
    public void AppStateReady_ClearsMaintenance()
    {
        SimulateKvChange(HostRosterKeys.Key("booking", "i-1"), Descriptor("booking", "i-1", "Maintenance"));
        SimulateKvChange(HostRosterKeys.Key("booking", "i-1"), Descriptor("booking", "i-1", "Ready"));

        _state.IsAnyInMaintenance.Should().BeFalse();
        _state.IsInMaintenance("booking").Should().BeFalse();
    }

    [Fact]
    public void AppStateDraining_AlsoTriggersMaintenanceFlag()
    {
        SimulateKvChange(HostRosterKeys.Key("booking", "i-1"), Descriptor("booking", "i-1", "Draining"));

        _state.IsAnyInMaintenance.Should().BeTrue();
    }

    [Fact]
    public void MultipleApps_OnlyAffectedAppTracked()
    {
        SimulateKvChange(HostRosterKeys.Key("booking", "i-1"), Descriptor("booking", "i-1", "Maintenance"));
        SimulateKvChange(HostRosterKeys.Key("billing", "i-2"), Descriptor("billing", "i-2", "Ready"));

        _state.IsInMaintenance("booking").Should().BeTrue();
        _state.IsInMaintenance("billing").Should().BeFalse();
        _state.IsAnyInMaintenance.Should().BeTrue();
    }

    // The roster is per instance; an app is in maintenance when every instance of it is.

    /// <summary>One instance of two draining leaves the app served by the other: not in maintenance.</summary>
    [Fact]
    public void OneInstanceOfTwo_InMaintenance_TheAppIsNot()
    {
        SimulateKvChange(HostRosterKeys.Key("stock", "a"), Descriptor("stock", "a", "Draining"));
        SimulateKvChange(HostRosterKeys.Key("stock", "b"), Descriptor("stock", "b", "Ready"));

        _state.IsInMaintenance("stock").Should().BeFalse("instance b still serves it");
        _state.IsAnyInMaintenance.Should().BeFalse();
    }

    [Fact]
    public void EveryInstance_InMaintenance_TheAppIs()
    {
        SimulateKvChange(HostRosterKeys.Key("stock", "a"), Descriptor("stock", "a", "Maintenance"));
        SimulateKvChange(HostRosterKeys.Key("stock", "b"), Descriptor("stock", "b", "Maintenance"));

        _state.IsInMaintenance("stock").Should().BeTrue();
        _state.IsAnyInMaintenance.Should().BeTrue();
    }

    /// <summary>Every instance drained: the app answers its maintenance status.</summary>
    [Fact]
    public void EveryInstanceDrained_TheAppIsInMaintenance()
    {
        SimulateKvChange(HostRosterKeys.Key("stock", "a"), Descriptor("stock", "a", "Drained"));
        SimulateKvChange(HostRosterKeys.Key("stock", "b"), Descriptor("stock", "b", "Drained"));

        _state.IsInMaintenance("stock").Should().BeTrue();
    }

    /// <summary>The instance that was serving leaves: the one left is in maintenance, so the app is.</summary>
    [Fact]
    public void TheAvailableInstanceLeaves_TheAppIsInMaintenance()
    {
        SimulateKvChange(HostRosterKeys.Key("stock", "a"), Descriptor("stock", "a", "Maintenance"));
        SimulateKvChange(HostRosterKeys.Key("stock", "b"), Descriptor("stock", "b", "Ready"));

        SimulateKvChange(HostRosterKeys.Key("stock", "b"), value: null, deleted: true);

        _state.IsInMaintenance("stock").Should().BeTrue();
    }

    /// <summary>The control: an app nobody lists is not in maintenance, whatever else is.</summary>
    [Fact]
    public void AnAppWithNoInstances_IsNotInMaintenance()
    {
        SimulateKvChange(HostRosterKeys.Key("stock", "a"), Descriptor("stock", "a", "Maintenance"));
        SimulateKvChange(HostRosterKeys.Key("stock", "a"), value: null, deleted: true);

        _state.IsInMaintenance("stock").Should().BeFalse();
        _state.IsAnyInMaintenance.Should().BeFalse();
    }

    [Fact]
    public void GlobalMaintenanceFlag_OverridesAll()
    {
        SimulateKvChange("state/gateway/maintenance", "true");

        _state.IsFullMaintenance.Should().BeTrue();
    }

    [Fact]
    public void GlobalMaintenanceFlag_Cleared()
    {
        SimulateKvChange("state/gateway/maintenance", "true");
        SimulateKvChange("state/gateway/maintenance", "false");

        _state.IsFullMaintenance.Should().BeFalse();
    }

    [Fact]
    public void NonStateKeys_Ignored()
    {
        SimulateKvChange("config/some-key", "value");
        SimulateKvChange("flags/new-ui", "true");

        _state.IsAnyInMaintenance.Should().BeFalse();
    }

    private static string Descriptor(string appId, string instanceId, string state)
        => System.Text.Json.JsonSerializer.Serialize(new HostDescriptorPayload
        {
            AppId = appId,
            AppName = appId,
            InstanceId = instanceId,
            State = state,
            StartedAt = System.DateTimeOffset.UnixEpoch,
            LastHeartbeat = System.DateTimeOffset.UnixEpoch
        });

    /// <summary>Simulates a KV change event from the Agent.</summary>
    private void SimulateKvChange(string key, string? value, bool deleted = false)
    {
        // Use reflection to call the private OnKvChanged method
        var method = typeof(MaintenanceState).GetMethod("OnKvChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method!.Invoke(_state, [new KvChangedPayload
        {
            Key = key,
            Value = value,
            Version = 1,
            Deleted = deleted
        }]);
    }
}
