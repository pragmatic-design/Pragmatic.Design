using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Diagnostics;
using Pragmatic.Actions.Mutation;
using Pragmatic.Caching;
using Pragmatic.Events;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Result;
using Pragmatic.Validation;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    // =========================================================================
    // Pipeline Stages — extracted from InvokeAsync for readability
    // =========================================================================

    /// <summary>
    ///     Delete pipeline: apply → soft/hard delete → persist → events → cache invalidation.
    /// </summary>
    private async Task<Result<TEntity, IError>> ExecuteDeletePipelineAsync(
        TMutation mutation, TEntity entity, string mutationType, bool commitsHere, Stopwatch stopwatch, Activity? activity, CancellationToken ct)
    {
        // 4-D. Apply pre-delete logic
        LogApplyingMutation(mutationType);
        var applyResult = await ApplyTheMutationAsync(mutation, entity, ct).ConfigureAwait(false);
        if (applyResult.IsFailure)
        {
            LogApplyFailed(mutationType, applyResult.Error.Code);
            return applyResult;
        }

        entity = applyResult.Value;

        // 5-D. Delete the entity (hard or soft) with compensation on failure
        LogDeletingEntity(mutationType);
        var softDeleteSnapshot = CaptureSoftDeleteState(entity);
        DeleteEntity(entity);

        // Null when this invocation owns the commit — including when it is the one that opened the
        // batch, which is the trap: reading BatchContext.Current here made the owner defer into its own
        // batch and nobody ever saved. Otherwise the batch of THIS unit of work, never the ambient one,
        // so a root holding one boundary cannot suppress the commit of a mutation in another.
        // Null when this invocation owns the commit: owning means no outer claim and no hand-opened
        // batch covering this unit of work, so there is nothing to defer into. Reading the ambient batch
        // here was the trap — the owner deferred into the batch it had just opened, and nobody saved.
        var batchContext = commitsHere
            ? null
            : Commit.CommitScope.BatchFor(UnitOfWork) ?? Commit.CommitScope.CoveringBatch(UnitOfWork);

        // Inside an explicit transaction the step saves and only its events wait: the next step has to
        // be able to read what this one wrote, which is the reason the transaction was asked for.
        if (batchContext is { DefersSave: false })
        {
            LogPersistingEntity(mutationType);
            await SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (batchContext is not null)
        {
            // In batch mode: accumulate entity, defer events and cache invalidation.
            // SaveChanges, event dispatch, and cache invalidation will be executed
            // when the batch is flushed — not here.
            LogBatchModeDeferred(mutationType);

            batchContext.AccumulateEntity(entity);
            if (entity is IHasDomainEvents eventsBatch)
            {
                foreach (var evt in eventsBatch.DomainEvents)
                    batchContext.DeferEvent(evt);
                eventsBatch.ClearDomainEvents();
            }

            // The mutation's [Raises<T>] events, built from the entity — but never before it is saved.
            // A [GeneratedValue("…{SEQ}…")] property is filled by the unit of work at the top of the
            // save, so an event constructed earlier carries the empty value and the handler cannot
            // tell: by the time it runs the row does have its number.
            //
            // Where this batch holds the save back too, nobody has saved yet when this invocation ends,
            // so what is deferred is the construction itself — performed at the flush, after the
            // owner's commit. Where it only holds the events, the save above has already run.
            if (batchContext.DefersSave)
                batchContext.DeferEventConstruction(() => CollectRaisedEvents(mutation, entity));
            else
                foreach (var evt in CollectRaisedEvents(mutation, entity))
                    batchContext.DeferEvent(evt);
        }
        else
        {
            // 6-D. Persist
            LogPersistingEntity(mutationType);
            try
            {
                await SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreSoftDeleteState(entity, softDeleteSnapshot);
                UnitOfWork?.Detach(entity);
                throw;
            }

            // 7+8-D. Post-commit side effects (events + cache), isolated from the committed outcome.
            // Built here, after the save, for the reason spelled out in the batch branch above.
            var declaredEvents = CollectRaisedEvents(mutation, entity);

            await RunPostCommitSideEffectsAsync(mutation, entity, declaredEvents, mutationType, ct).ConfigureAwait(false);
        }

        stopwatch.Stop();
        RecordMutationSuccess(activity, mutationType, stopwatch);
        LogSuccess(mutationType, stopwatch.ElapsedMilliseconds);
        return entity;
    }

    /// <summary>
    ///     Restore pipeline: apply → restore soft-delete → persist → events → cache invalidation.
    /// </summary>
    private async Task<Result<TEntity, IError>> ExecuteRestorePipelineAsync(
        TMutation mutation, TEntity entity, string mutationType, bool commitsHere, Stopwatch stopwatch, Activity? activity, CancellationToken ct)
    {
        // 4-R. Apply pre-restore logic
        LogApplyingMutation(mutationType);
        var applyResult = await ApplyTheMutationAsync(mutation, entity, ct).ConfigureAwait(false);
        if (applyResult.IsFailure)
        {
            LogApplyFailed(mutationType, applyResult.Error.Code);
            return applyResult;
        }

        entity = applyResult.Value;

        // 5-R. Restore the entity (reset soft-delete fields) with compensation on failure
        var restoreSnapshot = CaptureSoftDeleteState(entity);
        RestoreEntity(entity);

        // Null when this invocation owns the commit — including when it is the one that opened the
        // batch, which is the trap: reading BatchContext.Current here made the owner defer into its own
        // batch and nobody ever saved. Otherwise the batch of THIS unit of work, never the ambient one,
        // so a root holding one boundary cannot suppress the commit of a mutation in another.
        // Null when this invocation owns the commit: owning means no outer claim and no hand-opened
        // batch covering this unit of work, so there is nothing to defer into. Reading the ambient batch
        // here was the trap — the owner deferred into the batch it had just opened, and nobody saved.
        var batchContext = commitsHere
            ? null
            : Commit.CommitScope.BatchFor(UnitOfWork) ?? Commit.CommitScope.CoveringBatch(UnitOfWork);

        // Inside an explicit transaction the step saves and only its events wait: the next step has to
        // be able to read what this one wrote, which is the reason the transaction was asked for.
        if (batchContext is { DefersSave: false })
        {
            LogPersistingEntity(mutationType);
            await SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (batchContext is not null)
        {
            // In batch mode: accumulate entity, defer events and cache invalidation.
            // SaveChanges, event dispatch, and cache invalidation will be executed
            // when the batch is flushed — not here.
            LogBatchModeDeferred(mutationType);

            batchContext.AccumulateEntity(entity);
            if (entity is IHasDomainEvents eventsBatch)
            {
                foreach (var evt in eventsBatch.DomainEvents)
                    batchContext.DeferEvent(evt);
                eventsBatch.ClearDomainEvents();
            }

            // The mutation's [Raises<T>] events, built from the entity — but never before it is saved.
            // A [GeneratedValue("…{SEQ}…")] property is filled by the unit of work at the top of the
            // save, so an event constructed earlier carries the empty value and the handler cannot
            // tell: by the time it runs the row does have its number.
            //
            // Where this batch holds the save back too, nobody has saved yet when this invocation ends,
            // so what is deferred is the construction itself — performed at the flush, after the
            // owner's commit. Where it only holds the events, the save above has already run.
            if (batchContext.DefersSave)
                batchContext.DeferEventConstruction(() => CollectRaisedEvents(mutation, entity));
            else
                foreach (var evt in CollectRaisedEvents(mutation, entity))
                    batchContext.DeferEvent(evt);
        }
        else
        {
            // 6-R. Persist
            LogPersistingEntity(mutationType);
            try
            {
                await SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreSoftDeleteState(entity, restoreSnapshot);
                UnitOfWork?.Detach(entity);
                throw;
            }

            // 7+8-R. Post-commit side effects (events + cache), isolated from the committed outcome.
            // Built here, after the save, for the reason spelled out in the batch branch above.
            var declaredEvents = CollectRaisedEvents(mutation, entity);

            await RunPostCommitSideEffectsAsync(mutation, entity, declaredEvents, mutationType, ct).ConfigureAwait(false);
        }

        stopwatch.Stop();
        RecordMutationSuccess(activity, mutationType, stopwatch);
        LogSuccess(mutationType, stopwatch.ElapsedMilliseconds);
        return entity;
    }

    /// <summary>
    ///     Create/Update pipeline: auto-map → apply → L2 validate → persist → events → cache invalidation.
    /// </summary>
    private async Task<Result<TEntity, IError>> ExecuteCreateUpdatePipelineAsync(
        TMutation mutation, TEntity entity, bool isCreate, string mutationType, bool commitsHere, Stopwatch stopwatch, Activity? activity, CancellationToken ct)
    {
        // 4a. Apply auto-mapped properties (ALWAYS runs, even when dev overrides ApplyAsync)
        mutation.ApplyToEntity(entity);

        // 4a-bis. And the rows chosen by key, which ApplyToEntity cannot write: attaching a row named
        // only by its key means writing to the change tracker, and this assembly has no DbContext.
        // The generated override asks the repository, which does.
        await LinkChosenRowsAsync(mutation, entity, ct).ConfigureAwait(false);

        // 4a-ter. [TransitionsTo] with BeforeBody: the body sees the new state, and a move the state
        // machine refuses answers before any of the body's work is done.
        if (TransitionBeforeBody(entity) is { } refusedBefore)
        {
            LogApplyFailed(mutationType, refusedBefore.Code);
            return Result<TEntity, IError>.Failure(refusedBefore);
        }

        // 4b. Apply custom mutation logic (dev override)
        LogApplyingMutation(mutationType);
        var applyResult = await ApplyTheMutationAsync(mutation, entity, ct).ConfigureAwait(false);
        if (applyResult.IsFailure)
        {
            LogApplyFailed(mutationType, applyResult.Error.Code);
            return applyResult;
        }

        entity = applyResult.Value;

        // 4c. [TransitionsTo] with AfterBody — the body's own refusals came first — or, with ByBody, the
        // check that the body did move it. Before validation and invariants, which judge the final state.
        if (TransitionAfterBody(entity) is { } refusedAfter)
        {
            LogApplyFailed(mutationType, refusedAfter.Code);
            return Result<TEntity, IError>.Failure(refusedAfter);
        }

        EnsureTheBodyTransitioned(entity);

        // 5. Level 2: unified entity validation (change-aware, sync + async)
        var l2Error = await RunEntityValidationAsync(entity, isCreate, mutationType, activity, ct).ConfigureAwait(false);
        if (l2Error is not null)
            return Result<TEntity, IError>.Failure(l2Error);

        // 5b. Aggregate invariants ([Invariant] methods on the entity, and on the children this mutation
        // merged) — the last gate before persist on this path. Generated CheckInvariants override returns
        // the first violated invariant as an error; the default (no [Invariant] methods) returns null.
        // ⚠️ Here every rule is checked, because a mutation has written what it loaded. The other call
        // site is DomainActionInvoker.CheckLoadedInvariants, which checks only the rules its operation
        // can answer — those reading no navigation it left out of its Include list. Neither
        // covers a repository Add/Remove from a job or a seeding step: a rule that must hold there too
        // lives in the entity method that writes it.
        var invariantError = CheckInvariants(entity);
        if (invariantError is not null)
        {
            LogEntityValidationFailed(mutationType, invariantError.Code);
            return Result<TEntity, IError>.Failure(invariantError);
        }

        // 5b-bis. Temporal constraints ([TemporalRelation] MaxActive/overlap) — enforced on CREATE so
        // adding a new active period that breaches the constraint is rejected before persist rather than
        // left to the caller to check manually. Skipped on update: the record is already counted, so
        // re-checking it against itself would false-positive.
        if (isCreate)
        {
            var temporalError = CheckTemporalConstraints(entity);
            if (temporalError is not null)
            {
                LogEntityValidationFailed(mutationType, temporalError.Code);
                return Result<TEntity, IError>.Failure(temporalError);
            }
        }

        // 5c. IEntityLifecycle<TEntity>.OnSaving — last chance to modify the entity, after all validation
        // and before persistence. Runs for both create and update.
        InvokeOnSavingHooks(entity, BuildLifecycleContext());

        // 6. Persist
        LogPersistingEntity(mutationType);
        if (isCreate)
        {
            PersistNew(entity);
            var presetContext = BuildLifecycleContext();
            await ApplyPresetsAsync(entity, presetContext, ct).ConfigureAwait(false);
        }

        // Null when this invocation owns the commit — including when it is the one that opened the
        // batch, which is the trap: reading BatchContext.Current here made the owner defer into its own
        // batch and nobody ever saved. Otherwise the batch of THIS unit of work, never the ambient one,
        // so a root holding one boundary cannot suppress the commit of a mutation in another.
        // Null when this invocation owns the commit: owning means no outer claim and no hand-opened
        // batch covering this unit of work, so there is nothing to defer into. Reading the ambient batch
        // here was the trap — the owner deferred into the batch it had just opened, and nobody saved.
        var batchContext = commitsHere
            ? null
            : Commit.CommitScope.BatchFor(UnitOfWork) ?? Commit.CommitScope.CoveringBatch(UnitOfWork);

        // Inside an explicit transaction the step saves and only its events wait: the next step has to
        // be able to read what this one wrote, which is the reason the transaction was asked for.
        if (batchContext is { DefersSave: false })
        {
            LogPersistingEntity(mutationType);
            await SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (batchContext is not null)
        {
            // In batch mode: accumulate entity, defer events and cache invalidation.
            // SaveChanges, event dispatch, and cache invalidation will be executed
            // when the batch is flushed — not here.
            LogBatchModeDeferred(mutationType);

            batchContext.AccumulateEntity(entity);
            if (entity is IHasDomainEvents eventsBatch)
            {
                foreach (var evt in eventsBatch.DomainEvents)
                    batchContext.DeferEvent(evt);
                eventsBatch.ClearDomainEvents();
            }

            // The mutation's [Raises<T>] events, built from the entity — but never before it is saved.
            // A [GeneratedValue("…{SEQ}…")] property is filled by the unit of work at the top of the
            // save, so an event constructed earlier carries the empty value and the handler cannot
            // tell: by the time it runs the row does have its number.
            //
            // Where this batch holds the save back too, nobody has saved yet when this invocation ends,
            // so what is deferred is the construction itself — performed at the flush, after the
            // owner's commit. Where it only holds the events, the save above has already run.
            if (batchContext.DefersSave)
                batchContext.DeferEventConstruction(() => CollectRaisedEvents(mutation, entity));
            else
                foreach (var evt in CollectRaisedEvents(mutation, entity))
                    batchContext.DeferEvent(evt);
        }
        else
        {
            // A failure here IS a real failure and propagates — but the entity must stop being tracked
            // on the way out. Left pending it comes back at the next save through this unit of work,
            // so one refused row makes every later commit of the same request fail too, and PerStep
            // cannot keep what came before it. Measured on an import with one repeated key.
            try
            {
                await SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                UnitOfWork?.Detach(entity);
                throw;
            }

            // 7+8. Post-commit side effects (events + cache), isolated from the committed outcome.
            // Built here, after the save, for the reason spelled out in the batch branch above.
            var declaredEvents = CollectRaisedEvents(mutation, entity);

            await RunPostCommitSideEffectsAsync(mutation, entity, declaredEvents, mutationType, ct).ConfigureAwait(false);
        }

        stopwatch.Stop();
        RecordMutationSuccess(activity, mutationType, stopwatch);
        LogSuccess(mutationType, stopwatch.ElapsedMilliseconds);
        return entity;
    }

    /// <summary>
    ///     Runs Level 2 entity validation (IValidator&lt;TEntity&gt; or ISyncValidator fallback).
    /// </summary>
    private async Task<IError?> RunEntityValidationAsync(
        TEntity entity, bool isCreate, string mutationType, Activity? activity, CancellationToken ct)
    {
        IReadOnlySet<string>? modifiedProperties = null;
        if (!isCreate && entity is IChangeTracking tracking)
            modifiedProperties = tracking.ModifiedProperties;

        var entityValidator = _serviceProvider.GetService<Pragmatic.Validation.IValidator<TEntity>>();
        if (entityValidator is not null)
        {
            LogEntityValidation(mutationType);
            var validationResult = await entityValidator.ValidateAsync(entity, modifiedProperties, ct).ConfigureAwait(false);
            if (validationResult.IsFailure)
            {
                LogEntityValidationFailed(mutationType, validationResult.Code);
                RecordValidationFailure(mutationType, "L2");
                activity?.AddEvent(new ActivityEvent("validation.failed",
                    tags: new ActivityTagsCollection
                    {
                        { "validation.level", "L2" },
                        { "validation.error", validationResult.Code }
                    }));
                return validationResult;
            }
        }
        else if (entity is Pragmatic.Validation.ISyncValidator syncEntityValidator)
        {
            LogEntityValidation(mutationType);
            var syncResult = syncEntityValidator.Validate(modifiedProperties);
            if (syncResult.IsFailure)
            {
                LogEntityValidationFailed(mutationType, syncResult.Code);
                RecordValidationFailure(mutationType, "L2_sync");
                return syncResult;
            }
        }

        return null;
    }

    /// <summary>
    ///     The author's <c>ApplyAsync</c>, run inside an internal-call scope when the mutation declares
    ///     that it answers for what it invokes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[AbsorbsChildPermissions]</c> means the same thing on a mutation as on an action: it
    ///         covers what the body invokes, not only the children the mutation nests. Covering only the
    ///         nested children would leave a mutation calling another boundary's operation no way to
    ///         declare that it answers for it — the attribute would be there, and not apply.
    ///     </para>
    ///     <para>
    ///         ⚠️ The mutation's own permission is untouched: that check runs before this, in the filter
    ///         chain, and the scope covers only what the body calls.
    ///     </para>
    /// </remarks>
    private async Task<Result<TEntity, IError>> ApplyTheMutationAsync(
        TMutation mutation, TEntity entity, CancellationToken ct)
    {
        if (!AbsorbsChildPermissions)
            return await mutation.ApplyAsync(entity, ct).ConfigureAwait(false);

        var callContext = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<global::Pragmatic.Actions.Pipeline.ActionCallContext>(_serviceProvider);

        using var scope = callContext?.EnterInternalCall();
        return await mutation.ApplyAsync(entity, ct).ConfigureAwait(false);
    }
}
