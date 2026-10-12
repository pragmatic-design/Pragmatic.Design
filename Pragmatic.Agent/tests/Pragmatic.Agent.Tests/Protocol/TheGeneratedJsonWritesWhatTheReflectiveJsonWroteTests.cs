using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Gossip;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Platform;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Protocol.Serialization;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Protocol;

/// <summary>
///     Every JSON the Agent and its client write through generated metadata is byte for byte what the reflective
///     serializer wrote with the options each call site had (#142).
/// </summary>
/// <remarks>
///     The bytes are a contract twice over: a peer on another version reads the frame and the gossip datagram,
///     and the KV store and the snapshot file hold values written before the change. Each case serializes one
///     value both ways and compares; a context declared with the wrong options, or a payload whose name came
///     from a policy rather than its attribute, would differ here.
/// </remarks>
public class TheGeneratedJsonWritesWhatTheReflectiveJsonWroteTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 9, 30, 0, TimeSpan.FromHours(2));

    /// <summary>The options the frame had: <c>JsonWireFormat</c> before #142.</summary>
    private static readonly JsonSerializerOptions Frame = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The options a payload, a KV value or the lease had: none.</summary>
    private static readonly JsonSerializerOptions Default = new();

    public static TheoryData<object> Payloads => new()
    {
        new RegisterPayload { AppId = "app", AppName = "App", InstanceId = "i-1", Version = "1.0", ProcessId = 7, HostType = "Tenant", StartedAt = At },
        new RegisterPayload { AppId = "app", AppName = "App" },
        new HeartbeatPayload { AppId = "app", Health = "healthy", State = "Ready" },
        new StateReportPayload { AppId = "app", State = "maintenance" },
        new StateChangePayload { NewState = "Draining", Reason = null },
        new CommandPayload { CommandType = "DrainCommand", CommandId = "c-1", TargetHostId = "i-1", Data = JsonDocument.Parse("""{"a":1}""").RootElement },
        new HostDescriptorPayload { AppId = "app", AppName = "App", InstanceId = "i-1", StartedAt = At, LastHeartbeat = At },
        new InstanceAnnouncementPayload { Address = "http://h:5000", Path = "/api", RequireAuth = true, AppId = "app" },
        new KvSetPayload { Key = "k", Value = "v", ExpectedVersion = 3, Ephemeral = true },
        new KvChangedPayload { Key = "k", Value = null, Version = 4, Deleted = true },
        new KvEntriesPayload { Entries = [new KvEntryPayload { Key = "a", Value = "1", Version = 1, Found = true }] },
        new KvSetResultPayload { Version = 5, CasConflict = true },
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void APayload_IsWrittenAsBefore(object payload)
    {
        var generated = payload switch
        {
            RegisterPayload p => AgentPayloads.ToJson(p),
            HeartbeatPayload p => AgentPayloads.ToJson(p),
            StateReportPayload p => AgentPayloads.ToJson(p),
            StateChangePayload p => AgentPayloads.ToJson(p),
            CommandPayload p => AgentPayloads.ToJson(p),
            HostDescriptorPayload p => AgentPayloads.ToJson(p),
            InstanceAnnouncementPayload p => AgentPayloads.ToJson(p),
            KvSetPayload p => AgentPayloads.ToJson(p),
            KvChangedPayload p => AgentPayloads.ToJson(p),
            KvEntriesPayload p => AgentPayloads.ToJson(p),
            KvSetResultPayload p => AgentPayloads.ToJson(p),
            _ => throw new InvalidOperationException(payload.GetType().Name),
        };

        generated.Should().Be(JsonSerializer.Serialize(payload, payload.GetType(), Default));
    }

    [Fact]
    public void AFrame_IsWrittenAsBefore()
    {
        var frame = new AgentMessage
        {
            Type = MessageType.Response,
            Id = "c-1",
            Success = true,
            Error = null,
            Payload = AgentPayloads.ToElement(new KvSetResultPayload { Version = 2 }),
        };

        JsonWireFormat.Instance.Serialize(frame).Should().Equal(JsonSerializer.SerializeToUtf8Bytes(frame, Frame));
    }

    [Fact]
    public void AFrameWrittenBefore_IsReadTheSame()
    {
        var before = JsonSerializer.SerializeToUtf8Bytes(
            new AgentMessage { Type = MessageType.KvChanged, Id = "x", Payload = AgentPayloads.ToElement(new KvChangedPayload { Key = "k", Version = 1 }) },
            Frame);

        var read = JsonWireFormat.Instance.Deserialize<AgentMessage>(before)!;

        read.Type.Should().Be(MessageType.KvChanged);
        AgentPayloads.From<KvChangedPayload>(read.Payload!.Value)!.Key.Should().Be("k");
    }

    [Fact]
    public void AGossipDatagram_IsWrittenAsBefore()
    {
        var reflective = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var message = new GossipMessage
        {
            Type = GossipMessageType.Ping,
            SenderId = "agent-1",
            Members = [new MemberUpdate { Id = "agent-2", Host = "10.0.0.2", Port = 7946, State = MemberState.Alive, Incarnation = 3 }],
            KvUpdates = [new KvUpdate { Key = "k", Value = null, Version = 9, UpdatedAt = At, Owner = "agent-1" }],
        };

        JsonSerializer.SerializeToUtf8Bytes(message, GossipJsonContext.Default.GossipMessage)
            .Should().Equal(JsonSerializer.SerializeToUtf8Bytes(message, reflective));
    }

    [Fact]
    public void TheKvSnapshotFile_IsWrittenAsBefore()
    {
        var reflective = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        List<KvFilePersistence.KvEntryDto> entries =
        [
            new() { Key = "a", Value = "1", Version = 1, UpdatedAt = At, Owner = null },
            new() { Key = "b", Value = "2", Version = 2, UpdatedAt = At, Owner = "agent-1" },
        ];

        JsonSerializer.Serialize(entries, KvFileJsonContext.Default.ListKvEntryDto)
            .Should().Be(JsonSerializer.Serialize(entries, reflective));
    }

    [Fact]
    public void ALease_IsWrittenAsBefore()
    {
        var lease = new AgentClusterLeadership.LeaseRecord { HostId = "i-1", LeaseUntil = At };

        JsonSerializer.Serialize(lease, AgentLeaseJsonContext.Default.LeaseRecord)
            .Should().Be(JsonSerializer.Serialize(lease, Default));
    }

    public static TheoryData<HostCommand> Commands => new()
    {
        new DrainCommand(TimeSpan.FromSeconds(30)),
        new EnterMaintenanceCommand("upgrade", TimeSpan.FromMinutes(5)),
        new ExitMaintenanceCommand(),
        new MigrateCommand("tenant-*"),
    };

    /// <summary>Through the base type, as the control plane writes it: the <c>$type</c> discriminator included.</summary>
    [Theory]
    [MemberData(nameof(Commands))]
    public void ACommand_IsWrittenAsBefore(HostCommand command)
        => JsonSerializer.Serialize(command, AgentClientJsonContext.Default.HostCommand)
            .Should().Be(JsonSerializer.Serialize<HostCommand>(command, Default));

    [Fact]
    public void AnEvent_IsWrittenAndReadAsBefore()
    {
        ControlPlaneEvent[] events =
        [
            new HostStateChangedEvent("i-1", At, HostState.Ready, HostState.Draining, "deploy"),
            new ConfigChangedEvent("i-1", At, "config/x", "1", "2", null),
        ];

        foreach (var evt in events)
        {
            var before = JsonSerializer.Serialize(evt, Default);

            JsonSerializer.Serialize(evt, AgentClientJsonContext.Default.ControlPlaneEvent).Should().Be(before);
            JsonSerializer.Deserialize(before, AgentClientJsonContext.Default.ControlPlaneEvent).Should().Be(evt);
        }
    }

    /// <summary>The roster as the maintenance enforcer reads it: names in any case, as before.</summary>
    [Fact]
    public void TheEnforcer_ReadsADescriptorWhateverTheCase()
    {
        var read = JsonSerializer.Deserialize(
            """{"APPID":"app","appname":"App","InstanceId":"i-1","STATE":"Maintenance"}""",
            PlatformJsonContext.Default.HostDescriptorPayload)!;

        read.AppId.Should().Be("app");
        read.State.Should().Be("Maintenance");
    }
}
