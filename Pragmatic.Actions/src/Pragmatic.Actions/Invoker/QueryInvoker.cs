using System;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Identity;
using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     The pipeline a declared query goes through before it reads anything: its input is validated, its
///     permission is checked, and only then does the read run.
/// </summary>
/// <typeparam name="TQuery">The query type — the thing the permission is declared on.</typeparam>
/// <remarks>
///     <para>
///         A query is reached over HTTP and in process, and both doors go through this pipeline, so the
///         same query validates and asks for the same permission whichever door it came through. One
///         copy of each check, rather than one per door, is what keeps two doors from drifting apart.
///     </para>
///     <para>
///         ⚠️ <b>It cannot be the action filter chain.</b> <c>IActionFilter.BeforeExecuteAsync</c> is
///         constrained to <c>TAction : DomainAction&lt;TReturn&gt;</c> and a query is not one; widening
///         that constraint would touch every filter in the framework to serve one caller. What is shared
///         is the part that <em>decides</em> — <c>PermissionAuthorizationFilter.CheckRequirementAsync</c>,
///         the same code the action pipeline reaches — so a permission cannot come to mean two things
///         depending on which door it was asked at. What differs is only where the requirement comes
///         from: a registry for an action, the generated invoker itself for a query.
///     </para>
///     <para>
///         ⚠️ <b>The validator is the one Validation generates.</b> Validation emits an
///         <c>ISyncValidator</c> for any type whose members carry validation attributes, so a
///         <c>[Query]</c> with <c>[NotEmpty]</c> on a property has one. This calls it rather than
///         deriving a second, poorer copy of the same rules.
///     </para>
///     <para>
///         <b>What it does not do:</b> the policy (<c>FilterOrder.PolicyEvaluation</c>, 210) and the
///         resource authorizer (250). The generated query endpoint evaluates both itself, so HTTP is
///         covered and an in-process
///         invocation is not. Named here rather than left implicit: a pipeline that runs two of four
///         steps and does not say which is how a caller comes to believe it runs all four.
///     </para>
///     <para>
///         <b>No transaction, no commit scope.</b> A read writes nothing, and a reader that claimed a
///         commit scope would change who commits around it — a query invoked inside an action would
///         start deciding when that action's work is durable.
///     </para>
/// </remarks>
public abstract class QueryInvoker<TQuery>(IServiceProvider serviceProvider)
{
    /// <summary>The container the pipeline resolves the current user and the call context from.</summary>
    protected IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <summary>
    ///     Runs the pipeline and then the read.
    /// </summary>
    /// <typeparam name="TAnswer">What this particular read answers — a page, a list, or one row.</typeparam>
    /// <param name="query">The query, which is both the input to validate and the permission's subject.</param>
    /// <param name="read">
    ///     The read itself. Passed in rather than declared here because the shape depends on the query —
    ///     <c>PagedResult&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c> or a single row — and the pipeline
    ///     is the same for all three.
    /// </param>
    /// <param name="ct">The request's cancellation token.</param>
    /// <returns>What the read answered, or the first refusal.</returns>
    protected async Task<Result<TAnswer, IError>> RunAsync<TAnswer>(
        TQuery query,
        Func<TQuery, Task<Result<TAnswer, IError>>> read,
        CancellationToken ct = default)
    {
        var invalid = Validate(query);
        if (invalid is not null)
            return Result<TAnswer, IError>.Failure(invalid);

        var refused = await CheckPermissionAsync(ct).ConfigureAwait(false);
        if (refused is not null)
            return Result<TAnswer, IError>.Failure(refused);

        return await read(query).ConfigureAwait(false);
    }

    /// <summary>
    ///     The query's own validation, when the generator wrote one for it.
    /// </summary>
    /// <remarks>
    ///     No <c>modifiedProperties</c>: that set is what a partial write means by "the fields this
    ///     request touched", and a query has no partial shape — every input it carries was sent.
    /// </remarks>
    private static IError? Validate(TQuery query)
    {
        if (query is not Pragmatic.Validation.ISyncValidator validator)
            return null;

        var result = validator.Validate();
        return result.IsFailure ? result : (IError?)null;
    }

    /// <summary>
    ///     The permissions the query declares, or <c>null</c> when it declares none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Overridden by the generated invoker with the strings the author wrote in
    ///         <c>[RequirePermission]</c>. ⚠️ Not read from <c>IPermissionRequirementRegistry</c>: that
    ///         registry is built from the actions and mutations an assembly declares and has never
    ///         carried a query, so asking it would answer "nothing required" for every query and let
    ///         every caller through — a permission step that is indistinguishable from no step at all.
    ///     </para>
    ///     <para>
    ///         The generator that writes the invoker knows the permission, so it writes it. What the
    ///         registry exists for is the opposite case: a filter that is generic over the action and
    ///         cannot know which one it is running.
    ///     </para>
    /// </remarks>
    protected virtual string[]? RequiredPermissions => null;

    /// <summary>Whether every permission in <see cref="RequiredPermissions" /> is needed.</summary>
    /// <remarks>
    ///     <c>true</c> for <c>[RequirePermission]</c>, <c>false</c> for <c>[RequireAnyPermission]</c> —
    ///     the same two shapes an action declares.
    /// </remarks>
    protected virtual bool RequiresAllPermissions => true;

    /// <summary>
    ///     The permission declared on the query, decided by the same code the action pipeline uses.
    /// </summary>
    /// <remarks>
    ///     Only the way the requirement is <em>found</em> differs between an action and a query; the
    ///     decision — the mode, the unauthenticated caller, the wording of the refusal — is
    ///     <see cref="PermissionAuthorizationFilter.CheckRequirementAsync" /> in both.
    /// </remarks>
    private async Task<IError?> CheckPermissionAsync(CancellationToken ct)
    {
        var permissions = RequiredPermissions;
        if (permissions is null || permissions.Length == 0)
            return null;

        // Through the interface, like the mutation invoker — the concrete context is not the contract.
        var callContext = ServiceProvider.GetService(typeof(global::Pragmatic.Pipeline.ICallContext))
            as global::Pragmatic.Pipeline.ICallContext;
        if (callContext?.IsInternalCall == true)
            return null;

        var currentUser = ServiceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser
                          ?? AnonymousUser.Instance;

        var requirement = new PermissionAuthorizationFilter.PermissionRequirement(
            permissions,
            RequiresAllPermissions
                ? PermissionAuthorizationFilter.PermissionMode.All
                : PermissionAuthorizationFilter.PermissionMode.Any);

        var check = await PermissionAuthorizationFilter
            .CheckRequirementAsync(requirement, typeof(TQuery), currentUser, ct)
            .ConfigureAwait(false);

        return check.IsFailure ? check.Error : null;
    }
}
