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

    /// <summary>
    ///     Inside nested operations the entry records the innermost one, and the outermost (the origin: a job, an import, another
    ///     module's operation) nowhere (#750). Measured and kept as is here: recording the origin is a new hashed field, which breaks the
    ///     verification of every segment sealed before it, and that is the owner's decision (#772).
    /// </summary>
    [Fact]
    public void Entry_InsideNestedOperations_RecordsTheInnermost()
    {
        using (OperationScope.Enter("App.Imports.ImportOrdersAction"))
        using (OperationScope.Enter("App.Orders.PlaceOrderMutation"))
            AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, null).BusinessOperation.Should().Be("App.Orders.PlaceOrderMutation");
    }

    /// <summary>The tenant the caller acted in is the entry's (#750): it was never set.</summary>
    [Fact]
    public void Entry_RecordsTheCallersTenant()
    {
        AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, new User("anna", null, tenantId: "acme")).TenantId.Should().Be("acme");
        AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, new User("anna", null)).TenantId.Should().BeNull("no tenant, none recorded");
    }

    /// <summary>
    ///     Without a tracing listener the entries of one operation still share a correlation, and another operation's differs (#750): the
    ///     correlation was the trace id, null wherever nothing listens.
    /// </summary>
    [Fact]
    public void Entry_WithoutTracing_SharesACorrelationWithinOneOperation()
    {
        System.Diagnostics.Activity.Current = null;
        string? first, nested, other;

        using (OperationScope.Enter("App.Orders.PlaceOrderMutation"))
        {
            first = AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "42", Now, null).CorrelationId;
            using (OperationScope.Enter("App.Stock.ReserveMutation"))
                nested = AuditedChange.Entry(AuditedChange.EntityUpdated, "Stock", "7", Now, null).CorrelationId;
        }

        using (OperationScope.Enter("App.Orders.PlaceOrderMutation"))
            other = AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "43", Now, null).CorrelationId;

        first.Should().NotBeNull("the operation's correlation exists without tracing");
        nested.Should().Be(first, "a nested operation is part of the same logical operation");
        other.Should().NotBe(first, "another operation is another correlation");
        AuditedChange.Entry(AuditedChange.EntityCreated, "Order", "44", Now, null).CorrelationId.Should().BeNull("outside any operation and trace there is nothing to tie to");
    }

    private sealed class User(string id, IDelegationContext? delegation, string? tenantId = null) : ICurrentUser
    {
        public string Id => id;

        public string? DisplayName => id;

        public bool IsAuthenticated => true;

        public PrincipalKind Kind => PrincipalKind.User;

        public string? TenantId => tenantId;

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
