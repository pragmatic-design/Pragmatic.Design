using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Validation;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    /// <summary>
    ///     Checks the entity's aggregate invariants — the <c>[Invariant]</c> methods declared on the
    ///     entity, and on the children this mutation merged. The source generator overrides this when
    ///     either declares any; the default (no invariants) returns <see langword="null"/>. Returns the
    ///     first violated invariant as an <see cref="IError"/> (an
    ///     <see cref="Mutation.InvariantViolationError"/>), so invalid state does not reach the database
    ///     <b>through this mutation</b>. Runs after L2 validation, before persist.
    ///     <para>
    ///         ⚠️ Not through every write: an action that loads the same aggregate and changes it does
    ///         not come past here.
    ///     </para>
    /// </summary>
    protected virtual IError? CheckInvariants(TEntity entity) => null;

    /// <summary>
    ///     Enforces the entity's temporal constraints (<c>[TemporalRelation]</c> MaxActive / overlap).
    ///     The source generator overrides this for a temporal entity to call its generated
    ///     <c>ValidateTemporalConstraints</c> against the existing records; the default returns
    ///     <see langword="null"/>. Returns a <see cref="Persistence.Entity.TemporalOverlapError"/> when
    ///     violated, so a MaxActive/overlap breach is rejected (409) instead of silently persisted — the
    ///     pipeline enforces the validation rather than leaving it to an opt-in helper. Runs after
    ///     invariants, before persist.
    /// </summary>
    protected virtual IError? CheckTemporalConstraints(TEntity entity) => null;

    /// <summary>
    ///     The operations this write passes through, besides itself.
    /// </summary>
    /// <remarks>
    ///     A nested child is written by a direct call to its mapper, without going through its own
    ///     invoker: without this list nobody would check its <c>[RequirePermission]</c>, and the parent
    ///     would be a door around the child's rules. The generator overrides this list with the
    ///     children and — composed at runtime from their own lists — their descendants. Empty when
    ///     there are no children, which is the normal case.
    /// </remarks>
    protected virtual IReadOnlyList<Type> NestedOperations => [];

    /// <summary>
    ///     The nested children's rules, in depth.
    /// </summary>
    /// <remarks>
    ///     The validator generated for a type already walks its collections of validatables, but it
    ///     exists only for a type with rules of its <b>own</b>: a child without any does not get one,
    ///     and with it the loop over its descendants is lost. The generator overrides this method with
    ///     <c>ValidateNestedTree</c>, which rejoins the chain by composing it at runtime.
    /// </remarks>
    protected virtual global::Pragmatic.Validation.Types.ValidationError? ValidateNestedTree(TMutation mutation) => null;

    private async Task<IError?> CheckPermissionsAsync(string mutationType, Activity? activity, CancellationToken ct)
    {
        var permRegistry = _serviceProvider.GetService(typeof(IPermissionRequirementRegistry))
            as IPermissionRequirementRegistry;
        if (permRegistry is null)
            return null;

        var currentUser = _serviceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser
                          ?? AnonymousUser.Instance;
        // Through the interface — see the remark on PermissionAuthorizationFilter.CallContext.
        var callContext = _serviceProvider.GetService(typeof(global::Pragmatic.Pipeline.ICallContext))
            as global::Pragmatic.Pipeline.ICallContext;
        var permCheck = await PermissionAuthorizationFilter.CheckPermissionsForAsync(
            typeof(TMutation), currentUser, callContext?.IsInternalCall ?? false, permRegistry, ct).ConfigureAwait(false);
        if (permCheck.IsFailure)
        {
            LogFilterShortCircuit(mutationType, "PermissionCheck");
            activity?.SetTag(ActionTags.PermissionDenied, true);
            return permCheck.Error!;
        }

        // And the tree of nested operations. A child's permission must hold wherever that child is
        // reached, not only through its own door — which a mutation child does not even have, since
        // it is not exposed.
        foreach (var nested in NestedOperations)
        {
            var nestedCheck = await PermissionAuthorizationFilter.CheckPermissionsForAsync(
                nested, currentUser, callContext?.IsInternalCall ?? false, permRegistry, ct)
                .ConfigureAwait(false);
            if (nestedCheck.IsFailure)
            {
                LogFilterShortCircuit(mutationType, "PermissionCheck");
                activity?.SetTag(ActionTags.PermissionDenied, true);
                return nestedCheck.Error!;
            }
        }

        return null;
    }

    // Mutations are NOT DomainActions, so the global IActionFilter chain that DomainActionInvoker runs
    // (GetSortedGlobalFilters) does not apply to them — only the permission check above and the typed
    // IActionFilter<TMutation> filters do. That silently bypassed [RequirePolicy<T>] (PolicyEvaluationFilter)
    // and per-record IResourceAuthorizer<T> (ResourceAuthorizationFilter) for every mutation. Enforce both
    // here explicitly, in the same Order they would run for actions (policy 210 → resource 250).
    private async Task<IError?> CheckPolicyAndResourceAsync(
        TMutation mutation, string mutationType, Activity? activity, CancellationToken ct)
    {
        PolicyEvaluationFilter? policyFilter = null;
        ResourceAuthorizationFilter? resourceFilter = null;
        foreach (var filter in _serviceProvider.GetServices<IActionFilter>())
        {
            if (filter is PolicyEvaluationFilter p)
                policyFilter = p;
            else if (filter is ResourceAuthorizationFilter r)
                resourceFilter = r;
        }

        if (policyFilter is not null)
        {
            var policyResult = await policyFilter.EvaluatePolicyForAsync(typeof(TMutation)).ConfigureAwait(false);
            if (policyResult.IsFailure)
            {
                LogFilterShortCircuit(mutationType, "PolicyEvaluation");
                activity?.SetTag(ActionTags.PolicyDenied, true);
                return policyResult.Error!;
            }
        }

        if (resourceFilter is not null)
        {
            var resourceResult = await resourceFilter.AuthorizeResourceAsync(mutation, ct).ConfigureAwait(false);
            if (resourceResult.IsFailure)
            {
                LogFilterShortCircuit(mutationType, "ResourceAuthorization");
                activity?.SetTag(ActionTags.ResourceDenied, true);
                return resourceResult.Error!;
            }
        }

        return null;
    }

    private IError? RunL1SyncValidation(TMutation mutation, string mutationType, Activity? activity)
    {
        // B19: mirror ValidationFilter semantics — SG metadata drives what runs.
        // Without this, [NoValidation] on a mutation was silently ignored and nested
        // ISyncValidator properties were never validated (divergence from DomainActions).
        var metadata = mutation as IActionValidationMetadata;
        if (metadata is { HasNoValidation: true })
            return null;

        if (metadata is { RunSyncValidation: false })
            return null;

        if (mutation is ISyncValidator syncValidator)
        {
            var syncResult = syncValidator.Validate();
            if (syncResult.IsFailure)
            {
                LogSyncValidationFailed(mutationType, syncResult.Code);
                RecordValidationFailure(mutationType, "L1_sync");
                activity?.AddEvent(new ActivityEvent("validation.failed",
                    tags: new ActivityTagsCollection
                    {
                        { "validation.level", "L1_sync" },
                        { "validation.error", syncResult.Code }
                    }));
                return syncResult;
            }
        }

        // The children's tree, where the parent's validator does not reach.
        if (ValidateNestedTree(mutation) is { IsFailure: true } treeError)
        {
            LogSyncValidationFailed(mutationType, treeError.Code);
            RecordValidationFailure(mutationType, "L1_sync_nested");
            return treeError;
        }

        // Nested ISyncValidator properties — SG-generated direct property access, zero reflection.
        if (metadata?.ValidateNestedSync() is { IsFailure: true } nestedError)
        {
            LogSyncValidationFailed(mutationType, nestedError.Code);
            RecordValidationFailure(mutationType, "L1_sync_nested");
            activity?.AddEvent(new ActivityEvent("validation.failed",
                tags: new ActivityTagsCollection
                {
                    { "validation.level", "L1_sync_nested" },
                    { "validation.error", nestedError.Code }
                }));
            return nestedError;
        }

        return null;
    }

    private async Task<IError?> RunL1AsyncValidationAsync(
        TMutation mutation, string mutationType, Activity? activity, CancellationToken ct)
    {
        // B19: honor SG metadata like ValidationFilter does — [NoValidation] skips everything,
        // async validation is opt-in via [Validate] when metadata is available. Without
        // metadata (no SG output for this type) keep the legacy run-if-registered behavior.
        var metadata = mutation as IActionValidationMetadata;
        if (metadata is { HasNoValidation: true })
            return null;

        if (metadata is { RunAsyncValidation: false })
            return null;

        var asyncValidator = _serviceProvider.GetService<IAsyncValidator<TMutation>>();
        if (asyncValidator is null)
            return null;

        var asyncResult = await asyncValidator.ValidateAsync(mutation, ct).ConfigureAwait(false);
        if (asyncResult.IsSuccess)
            return null;

        LogAsyncValidationFailed(mutationType, asyncResult.Code);
        RecordValidationFailure(mutationType, "L1_async");
        activity?.AddEvent(new ActivityEvent("validation.failed",
            tags: new ActivityTagsCollection
            {
                { "validation.level", "L1_async" },
                { "validation.error", asyncResult.Code }
            }));
        return asyncResult;
    }

    private async Task<IError?> RunTypedFiltersAsync(
        TMutation mutation, string mutationType, Activity? activity, CancellationToken ct)
    {
        foreach (var filter in _sortedTypedFilters)
        {
            var filterResult = await filter.BeforeExecuteAsync(mutation, ct).ConfigureAwait(false);
            if (filterResult.IsFailure)
            {
                LogFilterShortCircuit(mutationType, filter.GetType().Name);
                activity?.SetTag(ActionTags.FilterDenied, filter.GetType().Name);
                return filterResult.Error!;
            }
        }

        return null;
    }
}
