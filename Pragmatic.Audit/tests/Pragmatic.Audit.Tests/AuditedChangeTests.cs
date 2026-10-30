using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Tests;

/// <summary>
///     The entry a write to an <c>[Audited]</c> entity records, as every store builds it: who acted, for whom, inside which operation.
/// </summary>
public class AuditedChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entry_ForAUserActingForThemselves_RecordsThemAsTheActor_AndNobodyOnWhoseBehalf()
    {
        var entry = AuditedChange.Entry(AuditedChange.EntityUpdated, "Order", "42", Now, new User("anna", delegation: null));

        (entry.ActorRef, entry.OnBehalfOfRef).Should().Be(("anna", (string?)null));
        (entry.Operation, entry.TargetType, entry.TargetId, entry.OccurredAt).Should().Be((AuditedChange.EntityUpdated, "Order", "42", Now));
        (entry.Category, entry.Outcome, entry.SegmentId).Should().Be((AuditCategory.Data, AuditOutcome.Success, ""));
    }

    [Fact]
    public void Entry_ForADelegatedCall_RecordsTheAgentAsTheActor_AndTheSubjectAsWhoItActedFor()
    {
        var entry = AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, new User("anna", new Delegation("anna", "assistant")));

        (entry.ActorRef, entry.OnBehalfOfRef).Should().Be(("assistant", "anna"));
    }

    [Fact]
    public void Entry_WithoutACaller_RecordsNoActor()
    {
        var entry = AuditedChange.Entry(AuditedChange.EntityDeleted, "Order", "42", Now, currentUser: null);

        (entry.ActorRef, entry.OnBehalfOfRef).Should().Be(((string?)null, (string?)null));
    }

    [Fact]
    public void Entry_InsideAnOperation_RecordsIt_AndOutsideOneRecordsNone()
    {
        using (OperationScope.Enter("App.Orders.PlaceOrderMutation"))
            AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, null).BusinessOperation.Should().Be("App.Orders.PlaceOrderMutation");

        AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, null).BusinessOperation.Should().BeNull();
    }

    private sealed class User(string id, IDelegationContext? delegation) : ICurrentUser
    {
        public string Id => id;

        public string? DisplayName => id;

        public bool IsAuthenticated => true;

        public PrincipalKind Kind => PrincipalKind.User;

        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => AnonymousUser.Instance.Claims;

        public IDelegationContext? Delegation => delegation;

        public IUserAuthorization Authorization => AnonymousUser.Instance.Authorization;

        public IAuthenticationContext Authentication => AnonymousUser.Instance.Authentication;
    }

    private sealed class Delegation(string subjectId, string actorId) : IDelegationContext
    {
        public string SubjectId => subjectId;

        public string ActorId => actorId;

        public ActorKind ActorKind => ActorKind.Service;

        public DelegationPolicy Policy => DelegationPolicy.Intersection;

        public string? Purpose => null;

        public string? GrantId => null;

        public DateTimeOffset? ExpiresAt => null;

        public IReadOnlyList<string> Chain => [actorId];
    }
}
