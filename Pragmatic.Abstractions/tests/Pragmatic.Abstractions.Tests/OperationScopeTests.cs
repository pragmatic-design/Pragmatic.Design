using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The ambient that lets the audit trail say which operation changed a row.
/// </summary>
/// <remarks>
///     Ambient state is where a forgotten <c>using</c> leaks into the next request, so the shape under
///     test is the restore, not the set. What it carries is a join key for the Article 30 register, and a
///     key that names the wrong operation is worse than one that names none.
/// </remarks>
public class OperationScopeTests
{
    [Fact]
    public void OutsideAnyOperation_ThereIsNone()
    {
        OperationScope.Current.Should().BeNull();
    }

    [Fact]
    public void InsideAnOperation_ItIsTheCurrentOne()
    {
        using (OperationScope.Enter("App.InviteMemberMutation"))
            OperationScope.Current.Should().Be("App.InviteMemberMutation");

        OperationScope.Current.Should().BeNull("the scope restores what it found");
    }

    [Fact]
    public void ANestedOperation_RestoresTheOuterOneRatherThanClearingIt()
    {
        // An action invoking a mutation is two nested operations. Clearing on the inner dispose would
        // leave everything the action does afterwards unattributed.
        using (OperationScope.Enter("App.ImportMembersAction"))
        {
            using (OperationScope.Enter("App.InviteMemberMutation"))
                OperationScope.Current.Should().Be("App.InviteMemberMutation");

            OperationScope.Current.Should().Be("App.ImportMembersAction");
        }

        OperationScope.Current.Should().BeNull();
    }

    [Fact]
    public void ABlankName_LeavesTheOuterOperationStanding()
    {
        // A caller that cannot name itself must not erase the operation it runs inside: the rows it
        // writes still belong to that operation.
        using (OperationScope.Enter("App.ImportMembersAction"))
        {
            using (OperationScope.Enter("   "))
                OperationScope.Current.Should().Be("App.ImportMembersAction");

            OperationScope.Current.Should().Be("App.ImportMembersAction");
        }
    }

    [Fact]
    public async Task ParallelFlows_DoNotSeeEachOthersOperation()
    {
        // The failure this shape exists to prevent: parallel event dispatch in one DI scope attributing
        // every change to whichever branch ran last.
        var observed = new string?[2];

        await Task.WhenAll(
            Task.Run(async () =>
            {
                using (OperationScope.Enter("App.First"))
                {
                    await Task.Delay(20).ConfigureAwait(false);
                    observed[0] = OperationScope.Current;
                }
            }),
            Task.Run(async () =>
            {
                using (OperationScope.Enter("App.Second"))
                {
                    await Task.Delay(10).ConfigureAwait(false);
                    observed[1] = OperationScope.Current;
                }
            }));

        observed.Should().Equal("App.First", "App.Second");
    }
}
