using Pragmatic.Authorization.Delegation;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Authorization.Tests.Delegation;

/// <summary>
///     Acting on someone's behalf from code, where there is no request to carry a token.
/// </summary>
/// <remarks>
///     The alternative this replaces is a full-permission system identity, which is what the framework
///     documented for background work: the choice taught was between anonymous and everything.
/// </remarks>
public class DelegationScopeTests
{
    [Fact]
    public void OutsideAnyScope_ThereIsNoDelegation()
        => DelegationScope.Value.Should().BeNull();

    [Fact]
    public void WithinAScope_TheSubjectAndActorAreBothVisible()
    {
        using (DelegationScope.Begin("u-subject", "job-nightly", ActorKind.Service, DelegationPolicy.GrantScoped, "digest"))
        {
            var d = DelegationScope.Value!;
            d.SubjectId.Should().Be("u-subject");
            d.ActorId.Should().Be("job-nightly");
            d.Policy.Should().Be(DelegationPolicy.GrantScoped);
            d.Purpose.Should().Be("digest");
        }

        DelegationScope.Value.Should().BeNull("the scope restores what it found");
    }

    [Fact]
    public void ScopesRestore_RatherThanClear()
    {
        using (DelegationScope.Begin("u-1", "a-outer"))
        {
            using (DelegationScope.Begin("u-2", "a-inner"))
                DelegationScope.Value!.ActorId.Should().Be("a-inner");

            DelegationScope.Value!.ActorId.Should().Be("a-outer",
                "the inner scope restores the outer one, it does not end delegation");
        }
    }

    /// <summary>
    ///     Nesting extends the chain. Replacing it would put the depth cap out of reach, and an
    ///     unbounded chain is a slow way back to full authority.
    /// </summary>
    [Fact]
    public void NestingExtendsTheChain()
    {
        using (DelegationScope.Begin("u-1", "a-outer"))
        {
            DelegationScope.Value!.Chain.Should().BeEmpty("one hop needs no chain");

            using (DelegationScope.Begin("u-1", "a-inner"))
                DelegationScope.Value!.Chain.Should().BeEquivalentTo("a-outer", "a-inner");
        }
    }

    [Fact]
    public async Task TheScopeFlowsAcrossAwaits()
    {
        using (DelegationScope.Begin("u-async", "a-1"))
        {
            await Task.Yield();
            await Task.Delay(1);

            DelegationScope.Value!.SubjectId.Should().Be("u-async",
                "an AsyncLocal is the point — the work happens after an await");
        }
    }

    [Fact]
    public void TheActorIsTheCaller_NotAnArgument()
    {
        var service = new DelegationService(new FakeCurrentUser("u-caller", delegation: null));

        using var _ = service.ActAs("u-subject", purpose: "run 92");

        DelegationScope.Value!.ActorId.Should().Be("u-caller",
            "an API where the caller names the actor lets it name an actor it is not");
    }

    [Fact]
    public void AnUnauthenticatedCallerCannotActForAnyone()
    {
        var service = new DelegationService(new UnauthenticatedUser());

        var act = () => service.ActAs("u-subject");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no actor*");
    }

    private sealed class UnauthenticatedUser : ICurrentUser
    {
        public string Id => "";
        public string? DisplayName => null;
        public bool IsAuthenticated => false;
        public PrincipalKind Kind => PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        public IUserAuthorization Authorization => throw new NotSupportedException();
        public IAuthenticationContext Authentication => throw new NotSupportedException();
    }
}
