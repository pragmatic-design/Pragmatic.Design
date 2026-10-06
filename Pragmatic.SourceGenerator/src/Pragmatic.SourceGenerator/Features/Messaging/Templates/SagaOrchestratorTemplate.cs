using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>{Saga}.Orchestrator.g.cs</c> — the compile-time orchestrator that:
///     1. Routes events to the correct saga method based on current state
///     2. Validates state transitions at compile-time
///     3. Dispatches resulting DomainActions via IMessageBus
///     4. Manages compensation chain on failure (reverse declaration order, only compensable steps)
///     5. Tracks per-step <c>TimeoutAt</c> via the repository and, on expiry, walks the
///        compensation chain from the current state before marking the instance TimedOut
///     6. Persists saga state via ISagaRepository
/// </summary>
internal sealed partial class SagaOrchestratorTemplate : LoggingTemplate
{
    /// <summary>The typed-metadata helper the compensation clone goes through — no reflection.</summary>
    private const string Info = "global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For";

    private readonly SagaModel _model;

    public SagaOrchestratorTemplate(SagaModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"[Saga<{_model.StateTypeShortName}>] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Orchestrator", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Diagnostics");
        AddUsing("Microsoft.Extensions.Logging");
        AddUsing("Pragmatic.Messaging");
        AddUsing("Pragmatic.Messaging.Saga");
        AddUsing("Pragmatic.Messaging.Diagnostics");
        AppendNamespace(_model.Namespace);
        AppendLine();

        // Wrap in partial of the user's saga class so Go-to-Definition on the saga type
        // surfaces both the user source and the nested SG-emitted Orchestrator.
        XmlSummary($"SG-generated partial for <see cref=\"{_model.TypeName}\"/>. Contains the nested saga orchestrator.");
        Class(_model.TypeName, RenderOuterBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    /// <summary>A fully qualified name as a <c>cref</c> takes it: the same name without <c>global::</c>.</summary>
    private static string Unqualified(string fqn)
        => fqn.StartsWith("global::", System.StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }

    private void RenderOuterBody()
    {
        XmlSummary($"SG-generated orchestrator for saga <see cref=\"{_model.TypeName}\"/>.");
        // The state type by its qualified name (without `global::`, which a cref does not take): this
        // file's namespace is the saga's, and an enum declared anywhere else — an `Enums` namespace, say —
        // does not resolve from here. CS1574 is a warning until a project generates documentation with
        // --warnaserror, and then it is an error in a file the author cannot edit.
        XmlSummary($"State type: <see cref=\"{Unqualified(_model.StateTypeFqn)}\"/>. Steps: {_model.Steps.Length}.");
        Class("Orchestrator", RenderOrchestratorBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true, Partial = true });
    }

    private void RenderOrchestratorBody()
    {
        var sagaFqn = string.IsNullOrEmpty(_model.Namespace)
            ? _model.TypeName
            : $"global::{_model.Namespace}.{_model.TypeName}";

        // Fields
        AppendLine($"private readonly {sagaFqn} _saga;");
        AppendLine($"private readonly ISagaRepository<{sagaFqn}> _repository;");
        AppendLine("private readonly IMessageBus _bus;");
        AppendLine("private readonly ILogger<Orchestrator> _logger;");
        // StartedAt is the only instant this orchestrator writes, and it comes from here rather than
        // DateTimeOffset.UtcNow in the generated body — the one time source no container can replace.
        AppendLine("private readonly global::System.TimeProvider _timeProvider;");
        AppendLine();

        // Constructor
        Constructor("Orchestrator", () =>
        {
            AppendLine("_saga = saga;");
            AppendLine("_repository = repository;");
            AppendLine("_bus = bus;");
            AppendLine("_logger = logger;");
            AppendLine("_timeProvider = timeProvider ?? global::System.TimeProvider.System;");
        }, new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = sagaFqn, Name = "saga" },
            new() { Type = $"ISagaRepository<{sagaFqn}>", Name = "repository" },
            new() { Type = "IMessageBus", Name = "bus" },
            new() { Type = "ILogger<Orchestrator>", Name = "logger" },
            new() { Type = "global::System.TimeProvider?", Name = "timeProvider", DefaultValue = "null" },
        });

        AppendLine();

        // HandleEventAsync — main dispatch method: one attempt, read again when a save loses.
        RenderHandleEventWithRetry();

        AppendLine();

        RenderHandleEventAsync(sagaFqn);

        AppendLine();

        // HandleTimeoutAsync — invoked by TimeoutRunner when TimeoutAt has elapsed.
        RenderHandleTimeoutAsync(sagaFqn);

        AppendLine();

        // Per-step private compensator helpers (JSON state → dispatch on bus).
        RenderCompensationDispatchers(sagaFqn);

        // IsValidTransition — compile-time state validation
        RenderIsValidTransition();

        AppendLine();

        // TimeoutRunner nested class — non-generic ISagaTimeoutRunner the BG service calls into.
        RenderTimeoutRunner(sagaFqn);

        AppendLine();

        RenderLoggerMessages();
    }

    private void RenderHandleEventAsync(string sagaFqn)
    {
        XmlSummary("One attempt: reads the saga, routes the event to the step for its current state, and saves.");
        Method("HandleEventOnceAsync", () =>
        {
            AppendLine($"using var activity = MessagingDiagnostics.ActivitySource.StartActivity(\"Saga.{_model.TypeName}\");");
            AppendLine($"activity?.SetTag(\"saga.type\", \"{_model.TypeName}\");");
            AppendLine("activity?.SetTag(\"saga.correlation_id\", correlationId);");
            AppendLine();

            // Load or create saga instance
            AppendLine("var instance = await _repository.FindByCorrelationAsync(correlationId, ct).ConfigureAwait(false);");
            AppendLine();

            // Start step
            if (_model.StartStep is not null)
            {
                AppendLine("// Handle saga start");
                AppendLine($"if (instance is null && eventType == typeof({_model.StartStep.EventTypeFqn}))");
                Block(() =>
                {
                    AppendLine($"LogSagaStarting(\"{_model.TypeName}\", correlationId);");
                    // Save-before-publish: run the step and capture its action, but commit the saga row
                    // (state + deadline, atomic) BEFORE publishing — so a crash before commit can never
                    // emit an action for a saga row that was never persisted.
                    RenderStepInvocationCapture(_model.StartStep, "_saga");

                    if (_model.StartStep.NextState is not null)
                    {
                        AppendLine($"_saga.State = {_model.StateTypeFqn}.{_model.StartStep.NextState};");
                    }

                    AppendLine("_saga.StartedAt = _timeProvider.GetUtcNow();");
                    AppendLine("_saga.CorrelationId = correlationId;");
                    AppendLine("_saga.Id = global::System.Guid.NewGuid();");
                    RenderSaveStepAndDeliver(_model.StartStep, "_saga");
                    AppendLine("return;");
                });
                AppendLine();
            }

            AppendLine("if (instance is null) return;");
            AppendLine();

            // State-based dispatch via switch expression
            AppendLine("// Route event to correct handler based on current state");
            for (var i = 0; i < _model.Steps.Length; i++)
            {
                var step = _model.Steps[i];
                if (step.IsStart) continue;

                foreach (var state in step.ValidStates)
                {
                    AppendLine($"if (instance.State.Equals({_model.StateTypeFqn}.{state}) && eventType == typeof({step.EventTypeFqn}))");
                    Block(() =>
                    {
                        AppendLine($"LogSagaStepExecuting(\"{_model.TypeName}\", \"{step.MethodName}\", \"{state}\");");

                        // Try-catch for compensation chain. The pre-step state is the one we use
                        // to drive the chain — the handler may mutate State on the happy path,
                        // but on exception we haven't reached SaveAsync yet so pre-step state wins.
                        AppendLine($"var preStepState_{i} = instance.State;");
                        AppendLine("try");
                        Block(() => RenderStepExecution(step));
                        // Business rejection (SagaRejectedException): compensate, mark Compensated (terminal),
                        // and do NOT rethrow — the rejection is a decision, not a transient fault, so the
                        // message must not be retried/dead-lettered.
                        AppendLine("catch (global::Pragmatic.Messaging.Saga.SagaRejectedException __rejected)");
                        Block(() =>
                        {
                            AppendLine($"LogSagaStepRejected(\"{_model.TypeName}\", \"{step.MethodName}\", __rejected.Message);");
                            RenderCompensationChainForFailedStep(step);
                            AppendLine("await _repository.MarkCompensatedAsync(instance.Id, ct).ConfigureAwait(false);");
                        });
                        // Lost to another writer: not a fault of this step. Neither Faulted nor
                        // compensated — HandleEventAsync reads the saga again and runs the step it now wants.
                        AppendLine("catch (global::Pragmatic.Messaging.Saga.SagaConcurrencyException)");
                        Block(() => AppendLine("throw;"));
                        AppendLine("catch (Exception ex)");
                        Block(() =>
                        {
                            AppendLine($"LogSagaStepFailed(\"{_model.TypeName}\", \"{step.MethodName}\", ex);");
                            // A non-rejection fault is treated as TRANSIENT — do NOT compensate here.
                            // Compensating on every retry would re-publish the whole chain each redelivery.
                            // Mark Faulted for visibility (a successful retry resets it to Active via SaveAsync)
                            // then rethrow so the delivery pipeline retries/dead-letters the fault.
                            AppendLine("await _repository.MarkFaultedAsync(instance.Id, ex.Message, ct).ConfigureAwait(false);");
                            AppendLine("throw;");
                        });

                        AppendLine("return;");
                    });
                    AppendLine();
                }
            }
        },
        "global::System.Threading.Tasks.Task",
        new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = "object", Name = "@event" },
            new() { Type = "global::System.Type", Name = "eventType" },
            new() { Type = "string", Name = "correlationId" },
            new() { Type = "CancellationToken", Name = "ct", DefaultValue = "default" },
        },
        AccessModifier.Private,
        modifiers: new MethodModifiers { IsAsync = true });
    }

    private void RenderStepExecution(SagaStepModel step)
    {
        // Invoke the step method on the persisted instance so any state the handler
        // mutates (e.g. AssignedRoom) is carried through SaveAsync. The DI-resolved
        // `_saga` is only used for the start step when the instance doesn't exist yet.
        if (step.NextState is not null)
            AppendLine("var __stateBeforeStep = instance.State;");

        // Save-before-publish: capture the step's resulting action first, apply the transition, commit
        // the saga state AND next deadline atomically, and only THEN publish the action. Publishing
        // before the commit could emit a duplicate action (the save then loses a concurrency race) or
        // an action for a state that never persisted.
        RenderStepInvocationCapture(step, "instance");

        if (step.NextState is not null)
        {
            // NextState is the DEFAULT (happy-path) transition: apply it only when the handler did
            // not itself set a state. A handler that transitions conditionally (e.g. to a Cancelled
            // state on a business rejection) wins — otherwise the declared NextState would silently
            // overwrite the handler's decision and the saga would advance down the wrong branch.
            AppendLine($"if (instance.State.Equals(__stateBeforeStep))");
            AppendLine($"    instance.State = {_model.StateTypeFqn}.{step.NextState};");
        }

        RenderSaveStepAndDeliver(step, "instance");
    }

    /// <summary>
    ///     Emits the step method call and captures its result (when the step returns one) into a local
    ///     <c>result</c> — WITHOUT publishing. Async steps (Task/ValueTask) are awaited: a sync call would
    ///     capture the Task object itself and fire-and-forget the step. Delivery is deferred to
    ///     <see cref="RenderSaveStepAndDeliver"/> so the saga state is committed first (save-before-publish).
    /// </summary>
    private void RenderStepInvocationCapture(SagaStepModel step, string target)
    {
        var call = $"{target}.{step.MethodName}(({step.EventTypeFqn})@event)";
        if (step.IsAsync)
            call = $"await {call}.ConfigureAwait(false)";

        if (step.ReturnTypeFqn is not null)
            AppendLine($"var result = {call};");
        else
            AppendLine($"{call};");
    }

    /// <summary>
    ///     Commits the saga state + step (atomic) and delivers the captured step result — the "deliver" half
    ///     of save-before-publish.
    ///     <para>
    ///     When the saga's boundary has a transactional outbox (<c>[EnableOutbox]</c>), the resulting action
    ///     is written to <c>__OutboxMessages</c> in the SAME transaction as the state (<c>SaveWithStepAndOutboxAsync</c>
    ///     returns true) and delivered exactly-once by the outbox pump — closing the W7 dual-write window. When
    ///     there is no outbox, the action is published inline AFTER the commit (save-before-publish): this still
    ///     removes the duplicate/orphan-action window, but a transport failure after the commit can leave the
    ///     action undelivered (the surrounding catch marks the saga Faulted and rethrows for redelivery). Make
    ///     handlers idempotent, or add <c>[EnableOutbox]</c> for exactly-once.
    ///     </para>
    /// </summary>
    private void RenderSaveStepAndDeliver(SagaStepModel step, string target)
    {
        var timeout = FormatTimeoutExpression(step);
        if (step.ReturnTypeFqn is null)
        {
            // No resulting action — a plain atomic state + step save.
            AppendLine($"await _repository.SaveWithStepAsync({target}, {timeout}, \"{step.MethodName}\", ct).ConfigureAwait(false);");
            return;
        }

        AppendLine("var __pending = result is not null");
        AppendLine("    ? new object[] { result }");
        AppendLine("    : global::System.Array.Empty<object>();");
        AppendLine($"var __delivered = await _repository.SaveWithStepAndOutboxAsync({target}, {timeout}, \"{step.MethodName}\", __pending, ct).ConfigureAwait(false);");
        AppendLine("if (!__delivered && result is not null)");
        AppendLine("    await _bus.PublishAsync(result, ct).ConfigureAwait(false);");
    }

    /// <summary>
    ///     Emits the compensation chain for a step that threw inside <c>HandleEventAsync</c>.
    ///     Chain = [failed step if compensable] + [every prior compensable step in reverse declaration order].
    ///     Each dispatcher is wrapped in a defensive try/catch so one compensator failure
    ///     does not abort the remainder of the chain.
    /// </summary>
    private void RenderCompensationChainForFailedStep(SagaStepModel failedStep)
    {
        var chain = BuildCompensationChainForFailure(failedStep);
        if (chain.Count == 0)
        {
            AppendLine("// No compensators on the chain for this step.");
            return;
        }

        RenderLoadExecutedSteps();
        foreach (var step in chain)
            RenderCompensatorDispatch(step);
    }

    /// <summary>
    ///     Loads the steps that actually executed (from the <c>__SagaSteps</c> history) into a set
    ///     used to filter the declaration-order compensation chain: only steps that really ran are
    ///     compensated, so a saga that branched does not compensate a step it never executed. An empty
    ///     set (a repository that keeps no history) falls back to compensating the whole chain.
    /// </summary>
    private void RenderLoadExecutedSteps()
    {
        AppendLine("var __executedSteps = await _repository.GetExecutedStepNamesAsync(instance.Id, ct).ConfigureAwait(false);");
        AppendLine("var __ranSteps = new global::System.Collections.Generic.HashSet<string>(__executedSteps);");
    }

    /// <summary>
    ///     Emits one compensator dispatch, guarded so it fires only for a step that actually ran (or when no
    ///     history is tracked), each wrapped defensively so one compensator failure never aborts the chain.
    /// </summary>
    private void RenderCompensatorDispatch(SagaStepModel step)
    {
        AppendLine($"if (__ranSteps.Count == 0 || __ranSteps.Contains(\"{step.MethodName}\"))");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("try");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"await DispatchCompensation_{step.MethodName}(instance, ct).ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("catch (Exception cex)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"LogSagaCompensationFailed(\"{_model.TypeName}\", \"{step.MethodName}\", cex);");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
    }

    private System.Collections.Generic.List<SagaStepModel> BuildCompensationChainForFailure(SagaStepModel failedStep)
    {
        // Walk the steps array in reverse declaration order, including the failed step itself
        // if compensable. "Prior" is defined by index (declaration order) — the MVP assumes
        // roughly linear sagas; multi-branch graphs are out of scope for the MVP chain.
        var chain = new System.Collections.Generic.List<SagaStepModel>();
        var failedIndex = -1;
        for (var i = 0; i < _model.Steps.Length; i++)
        {
            if (ReferenceEquals(_model.Steps[i], failedStep))
            {
                failedIndex = i;
                break;
            }
        }

        if (failedIndex < 0) return chain;

        for (var i = failedIndex; i >= 0; i--)
        {
            var step = _model.Steps[i];
            if (!string.IsNullOrEmpty(step.CompensationActionFqn))
                chain.Add(step);
        }

        return chain;
    }

    private void RenderHandleTimeoutAsync(string sagaFqn)
    {
        XmlSummary("Invoked by <see cref=\"TimeoutRunner\"/> when a saga's TimeoutAt has elapsed. Walks the compensation chain for the current state and marks the instance TimedOut.");
        Method("HandleTimeoutAsync", () =>
        {
            AppendLine($"using var activity = MessagingDiagnostics.ActivitySource.StartActivity(\"Saga.{_model.TypeName}.Timeout\");");
            AppendLine($"activity?.SetTag(\"saga.type\", \"{_model.TypeName}\");");
            AppendLine("activity?.SetTag(\"saga.correlation_id\", instance.CorrelationId);");
            AppendLine($"LogSagaTimedOut(\"{_model.TypeName}\", instance.CorrelationId, instance.State.ToString()!);");
            AppendLine();

            // Path-based compensation: load the steps that actually executed so each per-state
            // chain compensates only steps that really ran (empty history → the full chain).
            var anyTimeoutChain = false;
            foreach (var s in _model.StateValues)
            {
                if (BuildCompensationChainForTimedOutState(s).Count > 0)
                {
                    anyTimeoutChain = true;
                    break;
                }
            }

            if (anyTimeoutChain)
            {
                RenderLoadExecutedSteps();
                AppendLine();
            }

            // Emit a switch over state: for each state the saga can be in, dispatch the
            // chain of compensators for steps already executed up to that point.
            AppendLine("switch (instance.State)");
            AppendLine("{");
            IncreaseIndent();

            foreach (var stateName in _model.StateValues)
            {
                var chain = BuildCompensationChainForTimedOutState(stateName);
                AppendLine($"case {_model.StateTypeFqn}.{stateName}:");
                IncreaseIndent();
                if (chain.Count == 0)
                {
                    AppendLine("// No compensable steps have been executed at this state.");
                }
                else
                {
                    foreach (var step in chain)
                        RenderCompensatorDispatch(step);
                }
                AppendLine("break;");
                DecreaseIndent();
            }

            DecreaseIndent();
            AppendLine("}");
            AppendLine();

            AppendLine("await _repository.MarkTimedOutAsync(instance.Id, ct).ConfigureAwait(false);");
        },
        "global::System.Threading.Tasks.Task",
        new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = sagaFqn, Name = "instance" },
            new() { Type = "CancellationToken", Name = "ct", DefaultValue = "default" },
        },
        AccessModifier.Public,
        new MethodModifiers { IsAsync = true });
    }

    /// <summary>
    ///     Given a state the saga is parked in at timeout time, returns the chain of
    ///     compensable steps considered "already executed". An executed step is any
    ///     step whose <c>NextState</c> appears at or before the timed-out state in
    ///     declaration order (linear-saga approximation).
    /// </summary>
    private System.Collections.Generic.List<SagaStepModel> BuildCompensationChainForTimedOutState(string timedOutState)
    {
        // Find the last step whose NextState matches the timed-out state.
        // Then gather every compensable step up to and including that one, reversed.
        var boundaryIndex = -1;
        for (var i = 0; i < _model.Steps.Length; i++)
        {
            if (_model.Steps[i].NextState == timedOutState)
                boundaryIndex = i;
        }

        var chain = new System.Collections.Generic.List<SagaStepModel>();
        if (boundaryIndex < 0) return chain;

        for (var i = boundaryIndex; i >= 0; i--)
        {
            var step = _model.Steps[i];
            if (!string.IsNullOrEmpty(step.CompensationActionFqn))
                chain.Add(step);
        }

        return chain;
    }

    private void RenderCompensationDispatchers(string sagaFqn)
    {
        // One private helper per step that declares [CompensateWith<T>]. The helper
        // seeds the compensator from the saga state by mapping matching property names.
        // Compensator types that declare `required` members (or records with required
        // ctor parameters) would otherwise make a naive JSON round-trip throw because
        // the saga-state JSON does not carry those members. We deserialize with a
        // TypeInfoResolver modifier that clears `IsRequired`, so missing members are
        // simply left at their defaults instead of throwing.
        var emitted = new System.Collections.Generic.HashSet<string>();

        var hasAnyCompensator = false;
        foreach (var step in _model.Steps)
        {
            if (string.IsNullOrEmpty(step.CompensationActionFqn)) continue;
            hasAnyCompensator = true;
            break;
        }

        if (hasAnyCompensator)
        {
            AppendLine();
            AppendLine("// JSON options that ignore `required` members so seeding a compensator from");
            AppendLine("// partial saga state never throws for types with required props/ctor params.");
            AppendLine("private static readonly global::System.Text.Json.JsonSerializerOptions _compensationJsonOptions = new()");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("PropertyNameCaseInsensitive = true,");
            AppendLine("TypeInfoResolver = new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("Modifiers =");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("static typeInfo =>");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("if (typeInfo.Kind != global::System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)");
            AppendLine("    return;");
            AppendLine("foreach (var property in typeInfo.Properties)");
            AppendLine("    property.IsRequired = false;");
            DecreaseIndent();
            AppendLine("},");
            DecreaseIndent();
            AppendLine("},");
            DecreaseIndent();
            AppendLine("},");
            DecreaseIndent();
            AppendLine("};");
        }

        foreach (var step in _model.Steps)
        {
            if (string.IsNullOrEmpty(step.CompensationActionFqn)) continue;
            if (!emitted.Add(step.MethodName)) continue;

            AppendLine();
            AppendLine($"/// <summary>Seeds <see cref=\"{step.CompensationActionFqn}\"/> from the saga state (JSON) and publishes it.</summary>");
            AppendLine($"private async global::System.Threading.Tasks.Task DispatchCompensation_{step.MethodName}({sagaFqn} instance, CancellationToken ct)");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"var json = global::System.Text.Json.JsonSerializer.Serialize(instance, {Info}<{sagaFqn}>(_compensationJsonOptions));");
            AppendLine($"{step.CompensationActionFqn}? compensator;");
            AppendLine("try");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"compensator = global::System.Text.Json.JsonSerializer.Deserialize(json, {Info}<{step.CompensationActionFqn}>(_compensationJsonOptions));");
            DecreaseIndent();
            AppendLine("}");
            AppendLine("catch (global::System.Text.Json.JsonException)");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"LogSagaCompensationSkipped(\"{_model.TypeName}\", \"{step.MethodName}\");");
            AppendLine("return;");
            DecreaseIndent();
            AppendLine("}");
            AppendLine("if (compensator is null)");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"LogSagaCompensationSkipped(\"{_model.TypeName}\", \"{step.MethodName}\");");
            AppendLine("return;");
            DecreaseIndent();
            AppendLine("}");
            AppendLine("await _bus.PublishAsync(compensator, ct).ConfigureAwait(false);");
            AppendLine($"LogSagaCompensated(\"{_model.TypeName}\", \"{step.MethodName}\");");
            DecreaseIndent();
            AppendLine("}");
        }

        AppendLine();
    }

    private void RenderIsValidTransition()
    {
        XmlSummary("Compile-time validated state transitions.");
        Method("IsValidTransition", () =>
        {
            AppendLine("return (fromState, eventType) switch");
            AppendLine("{");
            IncreaseIndent();

            if (_model.StartStep is not null)
            {
                AppendLine($"(null, var t) when t == typeof({_model.StartStep.EventTypeFqn}) => true,");
            }

            foreach (var step in _model.Steps)
            {
                if (step.IsStart) continue;
                foreach (var state in step.ValidStates)
                {
                    AppendLine($"({_model.StateTypeFqn}.{state}, var t) when t == typeof({step.EventTypeFqn}) => true,");
                }
            }

            AppendLine("_ => false");
            DecreaseIndent();
            AppendLine("};");
        },
        "bool",
        new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = $"{_model.StateTypeFqn}", Name = "fromState", Nullable = true },
            new() { Type = "global::System.Type", Name = "eventType" },
        },
        modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderTimeoutRunner(string sagaFqn)
    {
        XmlSummary("Drives sagas past their <c>TimeoutAt</c> through the compensation chain. Registered as <see cref=\"global::Pragmatic.Messaging.Saga.ISagaTimeoutRunner\"/> in DI.");
        AppendLine($"internal sealed class TimeoutRunner : global::Pragmatic.Messaging.Saga.ISagaTimeoutRunner");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"private readonly global::Pragmatic.Messaging.Saga.ISagaRepository<{sagaFqn}> _repository;");
        AppendLine("private readonly Orchestrator _orchestrator;");
        AppendLine("private readonly ILogger<TimeoutRunner> _logger;");
        AppendLine();
        AppendLine($"public TimeoutRunner(global::Pragmatic.Messaging.Saga.ISagaRepository<{sagaFqn}> repository, Orchestrator orchestrator, ILogger<TimeoutRunner> logger)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("_repository = repository;");
        AppendLine("_orchestrator = orchestrator;");
        AppendLine("_logger = logger;");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        AppendLine("public async global::System.Threading.Tasks.Task RunDueTimeoutsAsync(global::System.DateTimeOffset asOf, global::System.Threading.CancellationToken ct)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var due = await _repository.GetDueForTimeoutAsync(asOf, ct).ConfigureAwait(false);");
        AppendLine("foreach (var instance in due)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("ct.ThrowIfCancellationRequested();");
        AppendLine("try");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("await _orchestrator.HandleTimeoutAsync(instance, ct).ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("catch (Exception ex)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"global::Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, ex, \"Saga timeout handler failed for {{SagaType}}\", \"{_model.TypeName}\");");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
    }

    private static readonly (string Type, string Name)[] SagaAndStep =
        [("string", "sagaType"), ("string", "stepName")];

    private void RenderLoggerMessages()
    {
        RenderLogMethod("LogSagaStarting", "Information",
            "Saga {SagaType} starting for correlation {CorrelationId}",
            [("string", "sagaType"), ("string", "correlationId")]);
        AppendLine();

        RenderLogMethod("LogSagaStepExecuting", "Debug",
            "Saga {SagaType} executing step {StepName} in state {CurrentState}",
            [..SagaAndStep, ("string", "currentState")]);
        AppendLine();

        RenderLogMethod("LogSagaStepFailed", "Error",
            "Saga {SagaType} step {StepName} failed",
            SagaAndStep, exception: "ex");
        AppendLine();

        RenderLogMethod("LogSagaStepRejected", "Information",
            "Saga {SagaType} step {StepName} rejected: {Reason}",
            [..SagaAndStep, ("string", "reason")]);
        AppendLine();

        RenderLogMethod("LogSagaCompensated", "Information",
            "Saga {SagaType} compensated step {StepName}",
            SagaAndStep);
        AppendLine();

        RenderLogMethod("LogSagaCompensationSkipped", "Warning",
            "Saga {SagaType} compensator for step {StepName} skipped — JSON seeding produced null",
            SagaAndStep);
        AppendLine();

        RenderLogMethod("LogSagaCompensationFailed", "Error",
            "Saga {SagaType} compensator for step {StepName} threw — continuing chain",
            SagaAndStep, exception: "ex");
        AppendLine();

        RenderLogMethod("LogSagaTimedOut", "Warning",
            "Saga {SagaType} timed out (correlation {CorrelationId}, state {State}) — running compensation chain",
            [("string", "sagaType"), ("string", "correlationId"), ("string", "state")]);
        AppendLine();

        RenderConflictLoggerMessage();
    }

    /// <summary>
    ///     Formats the C# expression used to compute the next <c>TimeoutAt</c> for a step.
    ///     Steps with no <c>[SagaTimeout]</c> clear the deadline; steps with one add the
    ///     declared duration to <c>DateTimeOffset.UtcNow</c>.
    /// </summary>
    private static string FormatTimeoutExpression(SagaStepModel step)
    {
        if (string.IsNullOrEmpty(step.TimeoutDuration))
            return "(global::System.DateTimeOffset?)null";

        return $"global::System.DateTimeOffset.UtcNow + global::System.TimeSpan.Parse(\"{step.TimeoutDuration}\")";
    }
}
