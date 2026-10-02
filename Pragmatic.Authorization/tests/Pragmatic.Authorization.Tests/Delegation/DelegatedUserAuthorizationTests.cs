using Pragmatic.Authorization.Delegation;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Authorization.Tests.Delegation;

/// <summary>
///     Composing the authority of a session where someone acts on behalf of someone else.
/// </summary>
/// <remarks>
///     <c>ImpersonatedBy</c> carries the impersonator's id. If nothing consults it when deciding, an
///     impersonated session is authorised exactly as the effective principal, and an actor with wider
///     permissions than the person it acts for simply uses them.
/// </remarks>
public class DelegatedUserAuthorizationTests
{
    private const string SubjectId = "u-subject";
    private const string ActorId = "a-agent";

    [Fact]
    public void WithoutDelegation_ForwardsTheSubjectsOwnAuthority()
    {
        var sut = Build(delegation: null, subject: ["orders.read", "orders.write"], actor: []);

        sut.Permissions.Should().BeEquivalentTo("orders.read", "orders.write");
        sut.HasPermission("orders.write").Should().BeTrue(
            "an ordinary session must be unaffected — that is nearly every session");
    }

    [Fact]
    public void Intersection_KeepsOnlyWhatBothSidesAllow()
    {
        var sut = Build(
            Delegation(DelegationPolicy.Intersection),
            subject: ["orders.read", "orders.write", "orders.delete"],
            actor: ["orders.read", "orders.write", "billing.read"]);

        sut.Permissions.Should().BeEquivalentTo("orders.read", "orders.write");
        sut.HasPermission("orders.delete").Should().BeFalse(
            "the subject can delete but the actor may not, so the delegated session may not");
        sut.HasPermission("billing.read").Should().BeFalse(
            "the actor can read billing but the subject cannot — an actor must not widen the subject");
    }

    /// <summary>
    ///     The failure this whole change exists to prevent.
    /// </summary>
    [Fact]
    public void Intersection_AnActorWiderThanItsSubject_DoesNotGainAuthority()
    {
        var sut = Build(
            Delegation(DelegationPolicy.Intersection),
            subject: ["orders.read"],
            actor: ["orders.read", "orders.delete", "admin.everything"]);

        sut.Permissions.Should().BeEquivalentTo("orders.read");
    }

    [Fact]
    public void Intersection_WithWildcards_MatchesInsteadOfComparingSets()
    {
        var sut = Build(
            Delegation(DelegationPolicy.Intersection),
            subject: ["billing.invoice.read", "billing.invoice.write", "orders.read"],
            actor: ["billing.*"]);

        sut.Permissions.Should().BeEquivalentTo("billing.invoice.read", "billing.invoice.write");
        sut.HasPermission("orders.read").Should().BeFalse(
            "the actor's wildcard does not reach outside billing");
    }

    /// <summary>
    ///     ⚠️ The mirror of the test above, and the one that was failing in an application.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The intersection walked the subject's permissions and asked whether the actor granted
    ///         each — which covers a wildcard held by the actor and not one held by the subject. A role
    ///         granted with <c>WithAllPermissions&lt;TBoundary&gt;()</c> holds <c>{boundary}.*</c> and
    ///         nothing else, so every delegation on that subject's behalf composed to an empty
    ///         authority: the most privileged role in an application was the only one that could not
    ///         act through an agent.
    ///     </para>
    ///     <para>
    ///         Measured on a consumer application before it was measured here — the same delegated
    ///         call answered 201 for a member and 403 for the owner, with nothing to say why. The
    ///         result is the narrower side stated concretely, not the wildcard and not nothing.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Intersection_WhenTheSubjectHoldsTheWildcard_KeepsWhatTheActorNames()
    {
        var sut = Build(
            Delegation(DelegationPolicy.Intersection),
            subject: ["workspaces.*"],
            actor: ["workspaces.member.update", "knowledge.term.read"]);

        sut.Permissions.Should().BeEquivalentTo("workspaces.member.update");
        sut.HasPermission("workspaces.member.update").Should().BeTrue();
        sut.HasPermission("knowledge.term.read").Should().BeFalse(
            "the subject's wildcard does not reach outside workspaces");
    }

    /// <summary>Both sides wildcards: the shared boundary survives, the others do not.</summary>
    [Fact]
    public void Intersection_WithWildcardsOnBothSides_KeepsTheSharedOne()
    {
        var sut = Build(
            Delegation(DelegationPolicy.Intersection),
            subject: ["workspaces.*", "work.*"],
            actor: ["workspaces.*"]);

        sut.Permissions.Should().BeEquivalentTo("workspaces.*");
        sut.HasPermission("workspaces.member.update").Should().BeTrue();
        sut.HasPermission("work.workitem.read").Should().BeFalse();
    }

    [Fact]
    public void SubjectOnly_GivesTheSubjectsAuthorityWhole()
    {
        var sut = Build(
            Delegation(DelegationPolicy.SubjectOnly),
            subject: ["orders.read", "orders.write"],
            actor: []);

        // Support reproducing a customer's problem needs the customer's authority, limits included.
        sut.Permissions.Should().BeEquivalentTo("orders.read", "orders.write");
    }

    /// <summary>
    ///     The case that broke the first design: a job's identity holds no user-level permissions, so
    ///     intersecting with it leaves nothing and the most common delegation of all does nothing.
    /// </summary>
    [Fact]
    public void GrantScoped_BoundsTheSubjectByTheGrant_NotByTheActorsOwnPermissions()
    {
        var sut = Build(
            Delegation(DelegationPolicy.GrantScoped),
            subject: ["orders.read", "orders.write", "orders.delete"],
            actor: [],                       // a job: no user-level permissions at all
            granted: ["orders.read", "orders.write"]);

        sut.Permissions.Should().BeEquivalentTo("orders.read", "orders.write");
    }

    [Fact]
    public void GrantScoped_WithAnEmptyGrant_GrantsNothing()
    {
        var sut = Build(
            Delegation(DelegationPolicy.GrantScoped),
            subject: ["orders.read"],
            actor: ["orders.read"],
            granted: []);

        sut.Permissions.Should().BeEmpty(
            "a grant that names no permission grants none — absent must never read as everything");
    }

    /// <summary>Roles, groups and scopes describe who the subject is; delegation does not rewrite them.</summary>
    [Fact]
    public void IdentityFacts_AreNotComposed()
    {
        var sut = Build(Delegation(DelegationPolicy.Intersection), subject: [], actor: []);

        sut.IsInRole("manager").Should().BeTrue();
        sut.Roles.Should().Contain("manager");
    }

    /// <summary>
    ///     🔴 A refused delegation denies. It must not fall back to the subject.
    /// </summary>
    /// <remarks>
    ///     The request is being made by the actor. Falling back to the subject's own authority looks
    ///     conservative and is the opposite — it hands the actor <em>more</em> than the delegation it
    ///     was refused would have, so an expired grant would become an upgrade. Written after three
    ///     tests caught exactly that.
    /// </remarks>
    [Fact]
    public void AnExpiredDelegation_GrantsNothing_NotTheSubjectsOwnAuthority()
    {
        var expired = new FakeDelegation(SubjectId, ActorId, DelegationPolicy.Intersection)
        {
            Expiry = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        var sut = Build(expired, subject: ["orders.read", "orders.write"], actor: ["orders.read"]);

        sut.Permissions.Should().BeEmpty();
        sut.HasPermission("orders.read").Should().BeFalse(
            "an inadmissible delegation authorises nothing at all");
    }

    [Fact]
    public void ACrossTenantDelegation_IsRefused_ByDefault()
    {
        var sut = Build(
            Delegation(DelegationPolicy.Intersection),
            subject: ["orders.read"],
            actor: ["orders.read"],
            actorTenant: "t-other");

        sut.Permissions.Should().BeEmpty(
            "on a boundary of isolation the default has to fail closed");
    }

    [Fact]
    public void AChainDeeperThanTheCap_IsRefused()
    {
        var deep = new FakeDelegation(SubjectId, ActorId, DelegationPolicy.Intersection)
        {
            ActorChain = ["a-1", "a-2", "a-3"],
        };

        var sut = Build(deep, subject: ["orders.read"], actor: ["orders.read"]);

        sut.Permissions.Should().BeEmpty(
            "three actors against a cap of two — an unbounded chain is a slow way back to full authority");
    }

    private static IDelegationContext Delegation(DelegationPolicy policy)
        => new FakeDelegation(SubjectId, ActorId, policy);

    private static DelegatedUserAuthorization Build(
        IDelegationContext? delegation,
        string[] subject,
        string[] actor,
        string[]? granted = null,
        string? actorTenant = null)
        => new(
            new FakeAuthorization(subject),
            new FakeCurrentUser(SubjectId, delegation),
            new FakeActorAuthority(actor, granted ?? [], actorTenant));
}
