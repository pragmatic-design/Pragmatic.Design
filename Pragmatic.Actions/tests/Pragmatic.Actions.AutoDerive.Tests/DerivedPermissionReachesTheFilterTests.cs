using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.AutoDerive.Tests;

[Boundary]
public partial class BillingBoundary;

/// <summary>An action with no <c>[RequirePermission]</c>: the switch is what gives it one.</summary>
/// <remarks>
///     Declared at namespace scope rather than nested in the test class. The generated invoker for a
///     nested action does not compile — it names the action unqualified while the base it overrides is
///     generic over the nested type (CS0534/CS0115). Recorded separately; it is not what this measures.
/// </remarks>
[DomainAction]
public partial class AddGuestCommentAction : DomainAction<string>
{
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success("ok"));
}

/// <summary>An action that says what it needs: derivation must leave it alone.</summary>
[DomainAction]
[Pragmatic.Authorization.RequirePermission("billing.written.by.hand")]
public partial class IssueRefundAction : DomainAction<string>
{
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success("ok"));
}

/// <summary>
///     The seam auto-derivation depends on: the registry the generator emits into a real compilation,
///     read through the interface <see cref="PermissionAuthorizationFilter" /> resolves.
/// </summary>
/// <remarks>
///     <para>
///         Both halves were covered and neither proved the pair. The generator tests assert the
///         registry's <i>text</i> in an in-memory compilation; the filter tests assert the filter
///         honours <i>a</i> registry, supplied by hand. Nothing tied the derived name to the type the
///         filter actually resolves — so a change to how a name is derived, which happened in the same
///         campaign as this test, could have shipped with every existing test green.
///     </para>
///     <para>
///         Enforcement itself is not repeated here: <c>PermissionAuthorizationFilterTests</c> already
///         proves the filter refuses a user lacking what the registry asks for, and it does not care
///         where the string came from. Neither is role expansion: a role grants a permission
///         <i>string</i>, and the Showcase suite already proves a role's grant reaches
///         <c>ICurrentUser.Authorization</c>. What neither can know is whether the string the generator
///         derived is the string those two exchange, which is what this measures.
///     </para>
/// </remarks>
public class DerivedPermissionReachesTheFilterTests
{
    private static IPermissionRequirementRegistry Registry() => new GeneratedPermissionRequirementRegistry();

    // Three segments, the shape a hand-written permission has — so a role file does not mix
    // catalog.property.read with billing.addguest-comment.
    [Fact]
    public void TheGeneratorDerivesTheNameARoleWouldGrant()
    {
        var requirement = Registry().GetRequirement(typeof(AddGuestCommentAction));

        requirement.Should().NotBeNull("the switch is on, so the action must carry a derived requirement");
        requirement!.Permissions.Should().BeEquivalentTo("billing.guest-comment.add");
    }

    [Fact]
    public void AnExplicitPermission_IsLeftExactlyAsWritten()
    {
        var requirement = Registry().GetRequirement(typeof(IssueRefundAction));

        requirement.Should().NotBeNull();

        // Collection literal, not a bare string: with TItem = string the params overload would take
        // the reason as a second expected element, and the failure would name something nobody wrote.
        requirement!.Permissions.Should().BeEquivalentTo(
            ["billing.written.by.hand"],
            "derivation only fires for an operation nobody gave a requirement");
    }
}
