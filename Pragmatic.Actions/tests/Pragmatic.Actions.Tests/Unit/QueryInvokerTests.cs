using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The pipeline a declared query goes through before it reads anything.
/// </summary>
/// <remarks>
///     <para>
///         A query had none: both decisions lived in the generated HTTP handler and nowhere else, so
///         reached in process a query validated nothing and asked nobody for a permission. ⚠️ Not that
///         the HTTP path was unvalidated — <c>QueryHandlerTemplate</c> emits an <c>is ISyncValidator</c>
///         into the handler. One copy per door is the defect; the doors are what drift.
///     </para>
///     <para>
///         ⚠️ It cannot be the action filter chain. <c>IActionFilter.BeforeExecuteAsync</c> is constrained
///         to <c>TAction : DomainAction&lt;TReturn&gt;</c>, and widening that to serve one caller would
///         touch every filter in the framework. What is shared is the part that decides:
///         <c>PermissionAuthorizationFilter.CheckPermissionsForAsync</c>, the same call the action
///         pipeline makes — so a permission cannot come to mean two things.
///     </para>
///     <para>
///         And no transaction: a read writes nothing, and a reader claiming a commit scope would change
///         who commits around it.
///     </para>
/// </remarks>
public class QueryInvokerTests
{
    /// <summary>The read runs, and answers.</summary>
    [Fact]
    public async Task AQueryThatValidatesAndIsPermitted_Reads()
    {
        var invoker = new TestInvoker<PlainQuery>(Services());

        var answer = await invoker.RunItAsync(new PlainQuery(), _ => Task.FromResult(Result<int, IError>.Success(7)));

        answer.IsSuccess.Should().BeTrue();
        answer.Value.Should().Be(7);
    }

    /// <summary>An input its own validator refuses never reaches the read.</summary>
    /// <remarks>
    ///     The validator is not new: <c>ValidationFeature</c> generates an <c>ISyncValidator</c> for any
    ///     type whose members carry validation attributes, so a <c>[Query]</c> class with <c>[NotEmpty]</c>
    ///     already has one, and only the handler ran it. ⚠️ The read must not run at all — answering the
    ///     validation error after touching the database would be a different, quieter defect.
    /// </remarks>
    [Fact]
    public async Task AnInputItsValidatorRefuses_NeverReachesTheRead()
    {
        var invoker = new TestInvoker<RefusedQuery>(Services());
        var read = false;

        var answer = await invoker.RunItAsync(new RefusedQuery(), _ =>
        {
            read = true;
            return Task.FromResult(Result<int, IError>.Success(7));
        });

        answer.IsFailure.Should().BeTrue();
        read.Should().BeFalse("validation runs before the read, not after it");
    }

    /// <summary>A caller without the permission is refused, and the read does not run.</summary>
    [Fact]
    public async Task ACallerWithoutThePermission_IsRefused()
    {
        var invoker = new GuardedInvoker<PlainQuery>(Services());
        var read = false;

        var answer = await invoker.RunItAsync(new PlainQuery(), _ =>
        {
            read = true;
            return Task.FromResult(Result<int, IError>.Success(7));
        });

        answer.IsFailure.Should().BeTrue();
        read.Should().BeFalse("the permission is checked before the query touches the database");
    }

    /// <summary>
    ///     The control: the same query, the same invoker, a caller who holds the permission.
    /// </summary>
    /// <remarks>
    ///     Without it "the permission is enforced" would be satisfied by an invoker that refuses
    ///     everyone — the failure this repository has met on the composite steps and on the boundary
    ///     interface.
    /// </remarks>
    [Fact]
    public async Task ACallerHoldingIt_Reads()
    {
        var invoker = new GuardedInvoker<PlainQuery>(Services(new StubUser("orders.read")));

        var answer = await invoker.RunItAsync(new PlainQuery(), _ => Task.FromResult(Result<int, IError>.Success(7)));

        answer.IsSuccess.Should().BeTrue();
        answer.Value.Should().Be(7);
    }

    private static IServiceProvider Services(ICurrentUser? user = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user ?? AnonymousUser.Instance);
        return services.BuildServiceProvider();
    }

    /// <summary>A query with no rules of its own.</summary>
    private sealed class PlainQuery;

    /// <summary>A query whose generated validator refuses it — what the source generator emits.</summary>
    private sealed class RefusedQuery : ISyncValidator
    {
        public ValidationError Validate() => ValidationError.For("Term", "validation.required");
    }

    /// <summary>The one thing a derived invoker has to say: how to read.</summary>
    /// <remarks>
    ///     ⚠️ Generic over the <b>concrete</b> query type, as the generated invoker is: the type is what
    ///     the refusal names, and what a caller reads in the error. The first version of this double
    ///     closed over <c>object</c>, and while the permission still came from a registry that made
    ///     every case ask about <c>object</c>, find no requirement, and the refusal case passed by being
    ///     allowed.
    /// </remarks>
    private sealed class TestInvoker<TQuery>(IServiceProvider services) : QueryInvoker<TQuery>(services)
    {
        public Task<Result<TAnswer, IError>> RunItAsync<TAnswer>(
            TQuery query, Func<TQuery, Task<Result<TAnswer, IError>>> read)
            => RunAsync(query, read, CancellationToken.None);
    }

    /// <summary>What the generated invoker of a query carrying <c>[RequirePermission]</c> looks like.</summary>
    /// <remarks>
    ///     The permission is a property the generator writes, not a registry lookup: the registry is
    ///     built from an assembly's actions and mutations and has never carried a query, so a query
    ///     asking it would be told nothing is required and would admit everyone.
    /// </remarks>
    private sealed class GuardedInvoker<TQuery>(IServiceProvider services) : QueryInvoker<TQuery>(services)
    {
        protected override string[]? RequiredPermissions => ["orders.read"];

        public Task<Result<TAnswer, IError>> RunItAsync<TAnswer>(
            TQuery query, Func<TQuery, Task<Result<TAnswer, IError>>> read)
            => RunAsync(query, read, CancellationToken.None);
    }

    private sealed class StubUser(params string[] permissions) : ICurrentUser
    {
        public string Id => "tester";
        public string? DisplayName => "tester";
        public string? TenantId => null;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization { get; } = new StubAuthorization([.. permissions]);
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed class StubAuthorization(HashSet<string> permissions) : IUserAuthorization
    {
        public IReadOnlySet<string> Permissions => permissions;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool HasAllPermissions(IEnumerable<string> required) => required.All(permissions.Contains);
        public bool HasAnyPermission(IEnumerable<string> required) => required.Any(permissions.Contains);
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }
}
