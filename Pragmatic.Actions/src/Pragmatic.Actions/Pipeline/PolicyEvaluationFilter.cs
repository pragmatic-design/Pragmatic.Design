using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Action filter that enforces <see cref="RequirePolicyAttribute{TPolicy}" /> on domain actions.
///     Runs at Order 210 (after permission check at 200, before resource authorization at 250).
/// </summary>
/// <remarks>
///     Currently evaluates only static policies declared via <c>[RequirePolicy&lt;T&gt;]</c> attributes.
///     Future: a DynamicPermissionFilter will handle entity-level <c>[DynamicPermission]</c> enforcement
///     where policies are stored in the database and resolved at runtime.
/// </remarks>
public sealed partial class PolicyEvaluationFilter(
    IServiceProvider serviceProvider,
    ILogger<PolicyEvaluationFilter> logger,
    IPolicyRegistry policyRegistry) : IActionFilter
{
    // Per-instance cache scoped to this provider — avoids cross-container stale entries
    private readonly ConcurrentDictionary<Type, ResourcePolicy?> _policyCache = new();
    private ICurrentUser? _currentUser;
    private ICurrentUser CurrentUser => _currentUser ??= serviceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser
                                                         ?? AnonymousUser.Instance;
    private global::Pragmatic.Pipeline.ICallContext? _callContext;
    /// <remarks>
    ///     Resolved through <c>ICallContext</c>, not the concrete type. Every writer — the event
    ///     dispatcher, the message bus, the generated boundary invokers — goes through the interface,
    ///     so a reader that went around it would leave an <c>ICallContext</c> substituted in DI
    ///     written to by the framework and read by nobody.
    /// </remarks>
    private global::Pragmatic.Pipeline.ICallContext CallContext =>
        _callContext ??= serviceProvider.GetService(typeof(global::Pragmatic.Pipeline.ICallContext))
            as global::Pragmatic.Pipeline.ICallContext
            ?? new ActionCallContext();

    /// <inheritdoc />
    public int Order => Filters.FilterOrder.PolicyEvaluation;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action, CancellationToken ct)
        where TAction : DomainAction<TReturn>
        => EvaluatePolicy(typeof(TAction));

    /// <inheritdoc />
    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action, Result<TReturn, IError> result, CancellationToken ct)
        where TAction : DomainAction<TReturn>
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteVoidAsync<TAction>(
        TAction action, CancellationToken ct)
        where TAction : VoidDomainAction
        => EvaluatePolicy(typeof(TAction));

    /// <inheritdoc />
    public Task AfterExecuteVoidAsync<TAction>(
        TAction action, VoidResult<IError> result, CancellationToken ct)
        where TAction : VoidDomainAction
        => Task.CompletedTask;

    /// <summary>
    ///     Mutation-pipeline entry point. Mutations aren't <see cref="DomainAction{TReturn}"/>s, so the
    ///     <see cref="IActionFilter"/> methods don't apply — the mutation invoker calls this to enforce
    ///     <c>[RequirePolicy&lt;T&gt;]</c> on the mutation type, same as for actions.
    /// </summary>
    internal Task<VoidResult<IError>> EvaluatePolicyForAsync(Type targetType)
        => EvaluatePolicy(targetType);

    private Task<VoidResult<IError>> EvaluatePolicy(Type actionType)
    {
        // Skip policy checks for internal (intra-boundary) calls
        if (CallContext.IsInternalCall)
        {
            LogSkippedInternalCall(actionType.Name);
            return Task.FromResult(VoidResult<IError>.Success());
        }

        var policy = _policyCache.GetOrAdd(actionType, policyRegistry.GetPolicy);

        if (policy is null)
        {
            LogNoPolicy(actionType.Name);
            return Task.FromResult(VoidResult<IError>.Success());
        }

        if (!CurrentUser.IsAuthenticated)
        {
            LogUnauthenticated(actionType.Name);
            return Task.FromResult(VoidResult<IError>.Failure(
                UnauthorizedError.Create("Authentication required")));
        }

        if (!policy.Evaluate(CurrentUser))
        {
            LogPolicyDenied(actionType.Name, CurrentUser.Id);
            return Task.FromResult(VoidResult<IError>.Failure(
                ForbiddenError.ActionDenied(actionType.Name)));
        }

        LogPolicyGranted(actionType.Name);
        return Task.FromResult(VoidResult<IError>.Success());
    }

    // =========================================================================
    // Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Trace, Message = "Action {ActionName} has no policy requirement, skipping")]
    private partial void LogNoPolicy(string actionName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Action {ActionName} policy check skipped — internal call")]
    private partial void LogSkippedInternalCall(string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionName} denied: user is not authenticated")]
    private partial void LogUnauthenticated(string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionName} policy denied for user {UserId}")]
    private partial void LogPolicyDenied(string actionName, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action {ActionName} policy evaluation passed")]
    private partial void LogPolicyGranted(string actionName);
}
