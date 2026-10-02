using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Delegation;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The delegation <c>[StartsDelegation]</c> opens has to still be open when the write
///     lands, because that is where the row is attributed.
/// </summary>
/// <remarks>
///     <para>
///         <c>OwnershipInterceptor</c> and <c>AuditingInterceptor</c> both stamp at
///         <c>SaveChanges</c>, and the invoker saves <b>after</b> the body returns. A scope that ends
///         with the body therefore attributes the row to the caller, whatever the declaration says: a
///         guest registered on behalf of someone comes back owned by the front desk, who then sees it
///         while the subject gets a 404.
///     </para>
///     <para>
///         ⚠️ The two tests differ only in <b>where</b> the scope is opened, and that is the point.
///         The second one pins the placement inside the body and its consequence, so the seam the first
///         one uses cannot be quietly undone: whoever moves the scope into the body will find a test
///         that says what that costs.
///     </para>
/// </remarks>
public class ADelegationCoversTheWriteItAttributesTests
{
    private sealed class RegisterForSomeoneAction : VoidDomainAction
    {
        public required string ForUserId { get; init; }

        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

    /// <summary>Records who the session was at save time — the question the interceptors ask.</summary>
    private sealed class SubjectRecordingUnitOfWork : IUnitOfWork
    {
        public string? SubjectAtSave { get; private set; }

        public bool Saved { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saved = true;
            SubjectAtSave = DelegationScope.Value?.SubjectId;
            return Task.FromResult(0);
        }

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException("this case does not open a transaction");

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Stands in for the generated invoker: the scope goes where the generator puts it.</summary>
    private sealed class GeneratedShapeInvoker(
        IServiceProvider serviceProvider, SubjectRecordingUnitOfWork unitOfWork, IDelegationService delegation)
        : VoidDomainActionInvoker<RegisterForSomeoneAction>(serviceProvider)
    {
        protected override void InjectDependencies(RegisterForSomeoneAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);

        protected override IDisposable? BeginInvocationScope(RegisterForSomeoneAction action)
            => delegation.ActAs(action.ForUserId, purpose: "registering on their behalf");
    }

    /// <summary>The placement this issue is about: opened around the body and gone by the save.</summary>
    private sealed class BodyScopedInvoker(
        IServiceProvider serviceProvider, SubjectRecordingUnitOfWork unitOfWork, IDelegationService delegation)
        : VoidDomainActionInvoker<RegisterForSomeoneAction>(serviceProvider)
    {
        protected override void InjectDependencies(RegisterForSomeoneAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);

        protected override async Task<VoidResult<IError>> ExecuteActionAsync(
            RegisterForSomeoneAction action, CancellationToken ct)
        {
            using var scope = delegation.ActAs(action.ForUserId, purpose: "registering on their behalf");
            return await base.ExecuteActionAsync(action, ct).ConfigureAwait(false);
        }
    }

    private sealed class TheCaller(string id) : Identity.ICurrentUser
    {
        public string Id { get; } = id;

        public bool IsAuthenticated => true;

        public string? DisplayName => Id;

        public Identity.PrincipalKind Kind => Identity.PrincipalKind.User;

        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            new Dictionary<string, IReadOnlyList<string>>();

        public Identity.IDelegationContext? Delegation => null;

        public Pragmatic.Authorization.IUserAuthorization Authorization =>
            throw new NotSupportedException("this case never asks a permission");

        public Identity.IAuthenticationContext Authentication =>
            throw new NotSupportedException("this case never asks how the caller signed in");
    }

    private static (IServiceProvider Services, SubjectRecordingUnitOfWork UnitOfWork, IDelegationService Delegation)
        Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return (services.BuildServiceProvider(),
            new SubjectRecordingUnitOfWork(),
            new DelegationService(new TheCaller("the-front-desk")));
    }

    [Fact]
    public async Task TheSubjectIsStillTheSession_WhenTheWriteLands()
    {
        var (services, unitOfWork, delegation) = Build();
        var invoker = new GeneratedShapeInvoker(services, unitOfWork, delegation);

        var result = await invoker.InvokeAsync(new RegisterForSomeoneAction { ForUserId = "the-subject" });

        result.IsSuccess.Should().BeTrue();
        unitOfWork.Saved.Should().BeTrue("the case is about the save, so there has to be one");

        unitOfWork.SubjectAtSave.Should().Be("the-subject",
            "the row is attributed at SaveChanges, and the declaration says it belongs to the subject");
    }

    /// <summary>
    ///     Why the seam exists, kept as a test rather than as a sentence: a scope that ends with the
    ///     body is already closed when the row is written.
    /// </summary>
    [Fact]
    public async Task AScopeOpenedAroundTheBodyAlone_IsGoneByTheSave()
    {
        var (services, unitOfWork, delegation) = Build();
        var invoker = new BodyScopedInvoker(services, unitOfWork, delegation);

        var result = await invoker.InvokeAsync(new RegisterForSomeoneAction { ForUserId = "the-subject" });

        result.IsSuccess.Should().BeTrue();
        unitOfWork.Saved.Should().BeTrue();

        unitOfWork.SubjectAtSave.Should().BeNull(
            "this is the wrong placement: the using has disposed before the invoker saves, "
            + "so the interceptors read the caller and the declaration buys nothing");
    }
}
