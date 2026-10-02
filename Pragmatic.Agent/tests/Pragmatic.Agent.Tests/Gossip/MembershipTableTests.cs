using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Gossip;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

public class MembershipTableTests
{
    private readonly MembershipTable _table = new("self-1", suspectTimeout: TimeSpan.FromMilliseconds(100));

    [Fact]
    public void ApplyUpdate_NewMember_AddsMember()
    {
        var update = new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 };

        _table.ApplyUpdate(update).Should().BeTrue();
        _table.GetAliveMembers().Should().HaveCount(1);
        _table.GetAliveMembers()[0].Id.Should().Be("peer-1");
    }

    [Fact]
    public void ApplyUpdate_SelfId_Ignored()
    {
        var update = new MemberUpdate { Id = "self-1", Host = "127.0.0.1", Port = 9900, State = MemberState.Alive, Incarnation = 1 };

        _table.ApplyUpdate(update).Should().BeFalse();
        _table.GetAliveMembers().Should().BeEmpty();
    }

    [Fact]
    public void ApplyUpdate_OlderIncarnation_Rejected()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 5 });
        var rejected = _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Suspect, Incarnation = 3 });

        rejected.Should().BeFalse();
        _table.GetAliveMembers().Should().HaveCount(1); // Still alive
    }

    [Fact]
    public void ApplyUpdate_HigherIncarnation_Accepted()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Suspect, Incarnation = 2 });

        _table.GetAliveMembers().Should().BeEmpty(); // Now suspect
    }

    [Fact]
    public void MarkSuspect_AliveBecomeSuspect()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });

        _table.MarkSuspect("peer-1");

        _table.GetAliveMembers().Should().BeEmpty();
        _table.GetAllMembers().Should().HaveCount(1);
        _table.GetAllMembers()[0].State.Should().Be(MemberState.Suspect);
    }

    [Fact]
    public void MarkAlive_SuspectBecomesAlive()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Suspect, Incarnation = 1 });

        _table.MarkAlive("peer-1");

        _table.GetAliveMembers().Should().HaveCount(1);
    }

    [Fact]
    public async Task EvictStaleMembers_SuspectBecomesDead()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });
        _table.MarkSuspect("peer-1");

        // Wait longer than suspect timeout (100ms)
        await Task.Delay(200);

        _table.EvictStaleMembers();

        _table.GetAllMembers().Should().HaveCount(1);
        _table.GetAllMembers()[0].State.Should().Be(MemberState.Dead);
    }

    [Fact]
    public void GetRandomAlive_ReturnsAliveOnly()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-2", Host = "10.0.0.3", Port = 9900, State = MemberState.Dead, Incarnation = 1 });

        var random = _table.GetRandomAlive();
        random.Should().NotBeNull();
        random!.Id.Should().Be("peer-1");
    }

    [Fact]
    public void GetRandomProbers_ExcludesTarget()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-2", Host = "10.0.0.3", Port = 9900, State = MemberState.Alive, Incarnation = 1 });
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-3", Host = "10.0.0.4", Port = 9900, State = MemberState.Alive, Incarnation = 1 });

        var probers = _table.GetRandomProbers("peer-1", count: 2);
        probers.Should().HaveCount(2);
        probers.Should().NotContain(m => m.Id == "peer-1");
    }

    [Fact]
    public void BuildMemberUpdates_ReturnsAllMembers()
    {
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });
        _table.ApplyUpdate(new MemberUpdate { Id = "peer-2", Host = "10.0.0.3", Port = 9900, State = MemberState.Suspect, Incarnation = 2 });

        var updates = _table.BuildMemberUpdates();
        updates.Should().HaveCount(2);
    }

    [Fact]
    public void OnMemberChanged_FiresOnStateTransition()
    {
        var fired = false;
        _table.OnMemberChanged += (_, _) => fired = true;

        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Alive, Incarnation = 1 });

        // First add doesn't fire (no state change — new member starts as Alive)
        // But ApplyUpdate sets state = update.State, and ClusterMember defaults to Alive
        // So it only fires when state CHANGES

        _table.ApplyUpdate(new MemberUpdate { Id = "peer-1", Host = "10.0.0.2", Port = 9900, State = MemberState.Suspect, Incarnation = 2 });

        fired.Should().BeTrue();
    }
}
