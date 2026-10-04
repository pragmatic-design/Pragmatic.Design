using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>{Handler}.Pipeline.g.cs</c> — a compile-time wrapper that weaves
///     resilience (retry, circuit breaker), idempotency, and logging around the handler's HandleAsync.
///     Conditionally includes cross-cutting concerns based on DetectedFeatures.
/// </summary>
internal sealed class HandlerPipelineTemplate : LoggingTemplate
{
    private readonly MessageHandlerModel _model;

    public HandlerPipelineTemplate(MessageHandlerModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"[MessageHandler] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Pipeline", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Diagnostics");
        AddUsing("Microsoft.Extensions.Logging");
        AddUsing("Pragmatic.Messaging");
        AddUsing("Pragmatic.Messaging.Diagnostics");

        if (_model.HasRetry || _model.HasCircuitBreaker || _model.HasConcurrencyLimit || _model.HasRateLimit)
            AddUsing("System.Threading");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Wrap in partial of the user's handler class so Go-to-Definition on the handler type
        // surfaces both the user source and the nested SG-emitted Pipeline.
        XmlSummary($"SG-generated partial for <see cref=\"{_model.TypeName}\"/>. Contains the nested pipeline wrapper.");
        Class(_model.TypeName, RenderOuterBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

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

    /// <summary>Namespace-qualified handler name — the [OnBus] map and idempotency keys are keyed on it.</summary>
    private string HandlerFqn => string.IsNullOrEmpty(_model.Namespace)
        ? _model.TypeName
        : $"{_model.Namespace}.{_model.TypeName}";

    private void RenderOuterBody()
    {
        XmlSummary($"Generated pipeline wrapper for <see cref=\"{_model.TypeName}\"/>.");
        Class("Pipeline", RenderPipelineBody,
            interfaces: new System.Collections.Generic.List<string>
            {
                $"global::Pragmatic.Messaging.IMessageHandler<{_model.MessageTypeFqn}>",
                "global::Pragmatic.Messaging.IPipelineWrappedHandler",
            },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true, Partial = true });
    }

    private void RenderPipelineBody()
    {
        RenderFields();
        AppendLine();
        RenderConstructor();
        AppendLine();
        RenderHandlerInterfaceBridge();
        AppendLine();

        // Circuit breaker static state (nested class, per-handler type)
        if (_model.HasCircuitBreaker)
        {
            RenderCircuitBreakerState();
            AppendLine();
        }

        // ExecuteAsync
        XmlSummary("Executes the handler with resilience, idempotency, and observability.");
        Method("ExecuteAsync", RenderExecuteBody,
            "global::System.Threading.Tasks.Task",
            new System.Collections.Generic.List<MethodParameter>
            {
                new() { Type = _model.MessageTypeFqn, Name = "message" },
                new() { Type = "MessageContext", Name = "context" },
                new() { Type = "CancellationToken", Name = "ct", DefaultValue = "default" },
            },
            AccessModifier.Public,
            new MethodModifiers { IsAsync = true });

        AppendLine();

        RenderLoggerMessages();
    }

    // ── Fields + Constructor ──

    private void RenderFields()
    {
        AppendLine($"private readonly {_model.TypeName} _handler;");
        AppendLine("private readonly ILogger<Pipeline> _logger;");

        // Idempotency store — always nullable, resolved from DI (null when not registered)
        AppendLine("private readonly global::Pragmatic.Messaging.IIdempotencyStore? _idempotencyStore;");

        // Middleware chain around the handler. Always injected (DI
        // provides an empty enumerable when no middleware is registered).
        AppendLine("private readonly global::System.Collections.Generic.IReadOnlyList<global::Pragmatic.Messaging.IMessageMiddleware> _middlewares;");

        if (_model.FeatureHasAuthorization)
            AppendLine("private readonly global::Pragmatic.Pipeline.ICallContext? _callContext;");

        if (_model.HasRedelivery)
            AppendLine("private readonly global::Pragmatic.Messaging.IMessageScheduler? _scheduler;");

        if (_model.HasConcurrencyLimit)
        {
            Comment($"Concurrency limit: max {_model.MaxConcurrent} concurrent execution(s), per process");
            AppendLine($"private static readonly SemaphoreSlim __concurrencyGate = new({_model.MaxConcurrent}, {_model.MaxConcurrent});");
        }

        if (_model.HasRateLimit)
        {
            Comment($"Rate limit: {_model.RatePermitsPerPeriod} execution(s) per {_model.RatePeriodSeconds}s window, per process");
            AppendLine("private static long __rateWindowStartTicks;");
            AppendLine("private static int __rateWindowCount;");
        }
    }

    private void RenderConstructor()
    {
        // Optional services default to null so the MS.DI container can construct the pipeline
        // directly (AddScoped<IMessageHandler<T>, Pipeline>) when they are not registered.
        var ctorParams = new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = _model.TypeName, Name = "handler" },
            new() { Type = "ILogger<Pipeline>", Name = "logger" },
            new() { Type = "global::System.Collections.Generic.IEnumerable<global::Pragmatic.Messaging.IMessageMiddleware>", Name = "middlewares" },
            new() { Type = "global::Pragmatic.Messaging.IIdempotencyStore", Name = "idempotencyStore", Nullable = true, DefaultValue = "null" },
        };

        if (_model.FeatureHasAuthorization)
            ctorParams.Add(new MethodParameter { Type = "global::Pragmatic.Pipeline.ICallContext", Name = "callContext", Nullable = true, DefaultValue = "null" });

        if (_model.HasRedelivery)
            ctorParams.Add(new MethodParameter { Type = "global::Pragmatic.Messaging.IMessageScheduler", Name = "scheduler", Nullable = true, DefaultValue = "null" });

        Constructor("Pipeline", () =>
        {
            AppendLine("_handler = handler;");
            AppendLine("_logger = logger;");
            AppendLine("_idempotencyStore = idempotencyStore;");
            AppendLine("_middlewares = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.OrderBy(middlewares, m => m.Order));");
            if (_model.FeatureHasAuthorization)
                AppendLine("_callContext = callContext;");
            if (_model.HasRedelivery)
                AppendLine("_scheduler = scheduler;");
        }, ctorParams, AccessModifier.Public);
    }

    // ── IMessageHandler<T> / IPipelineWrappedHandler bridge ──
    // The pipeline is what DI registers as IMessageHandler<T>, so the bus transparently
    // executes retry/CB/timeout/idempotency. Order forwards to the wrapped handler so
    // user-defined ordering survives the wrapping.

    private void RenderHandlerInterfaceBridge()
    {
        AppendLine("/// <inheritdoc />");
        AppendLine($"public int Order => ((global::Pragmatic.Messaging.IMessageHandler<{_model.MessageTypeFqn}>)_handler).Order;");
        AppendLine();
        AppendLine("/// <inheritdoc />");
        AppendLine($"public string HandlerTypeName => \"{HandlerFqn}\";");
        AppendLine();
        AppendLine("/// <inheritdoc />");
        AppendLine($"public global::System.Threading.Tasks.Task HandleAsync({_model.MessageTypeFqn} message, MessageContext context, CancellationToken ct = default)");
        AppendLine("    => ExecuteAsync(message, context, ct);");
    }

    // ── Circuit Breaker State (static, per handler type) ──

    private void RenderCircuitBreakerState()
    {
        var threshold = _model.CbFailureThreshold;
        var breakDuration = _model.CbBreakDurationSeconds;

        Comment($"Circuit breaker: {threshold} failures → open for {breakDuration}s");
        AppendLine("private static class CircuitState");
        Block(() =>
        {
            AppendLine("private static int _failureCount;");
            AppendLine("private static long _openUntilTicks;");
            AppendLine();
            AppendLine($"public static bool IsOpen => Volatile.Read(ref _failureCount) >= {threshold}");
            AppendLine("    && Stopwatch.GetTimestamp() < Volatile.Read(ref _openUntilTicks);");
            AppendLine();
            AppendLine("public static void RecordFailure()");
            Block(() =>
            {
                AppendLine($"if (Interlocked.Increment(ref _failureCount) >= {threshold})");
                AppendLine($"    Volatile.Write(ref _openUntilTicks, Stopwatch.GetTimestamp() + (long)({breakDuration} * Stopwatch.Frequency));");
            });
            AppendLine();
            AppendLine("public static void RecordSuccess() => Volatile.Write(ref _failureCount, 0);");
        });
    }

    // ── ExecuteAsync body ──

    private void RenderExecuteBody()
    {
        var messageName = _model.MessageTypeShortName;

        // 1. Telemetry — Activity span (always)
        AppendLine($"using var activity = MessagingDiagnostics.ActivitySource.StartActivity(\"Handler.{_model.TypeName}\");");
        AppendLine($"activity?.SetTag(\"messaging.handler\", \"{_model.TypeName}\");");
        AppendLine($"activity?.SetTag(\"messaging.message_type\", \"{messageName}\");");

        if (_model.FeatureHasMultiTenancy)
            AppendLine("if (context.TenantId is not null) activity?.SetTag(\"messaging.tenant_id\", context.TenantId);");

        AppendLine("activity?.SetTag(\"messaging.message_id\", context.MessageId);");

        if (_model.HasRedelivery)
        {
            Comment("Redelivery generation — RetryCount is the persisted redelivery counter");
            AppendLine("var __deliveryCount = context.RetryCount;");
        }

        AppendLine();

        // 2. Idempotency guard (always generated, null-safe)
        // Key is handler-scoped: the bare MessageId is already claimed by TransportAwareMessageBus
        // on the dispatch path, and fan-out to multiple handlers must not dedupe across handlers.
        AppendLine("// Idempotency check — skip duplicates when store is registered (handler-scoped key)");
        AppendLine("if (_idempotencyStore is not null)");
        Block(() =>
        {
            AppendLine($"var isNew = await _idempotencyStore.TryMarkAsProcessedAsync(context.MessageId + \":{HandlerFqn}\", ct).ConfigureAwait(false);");
            AppendLine("if (!isNew)");
            Block(() =>
            {
                AppendLine($"LogDuplicateSkipped(\"{messageName}\", \"{_model.TypeName}\", context.MessageId);");
                AppendLine("MessagingDiagnostics.IdempotencyDuplicates.Add(1);");
                AppendLine("return;");
            });
        });
        AppendLine();

        // 3. Circuit breaker gate (conditional)
        if (_model.HasCircuitBreaker)
        {
            AppendLine("// Circuit breaker — reject if open");
            AppendLine("if (CircuitState.IsOpen)");
            Block(() =>
            {
                AppendLine($"LogCircuitOpen(\"{messageName}\", \"{_model.TypeName}\");");
                AppendLine("MessagingDiagnostics.CircuitBreakerTrips.Add(1);");
                AppendLine("throw new global::System.InvalidOperationException(");
                AppendLine($"    $\"Circuit breaker open for handler {_model.TypeName}\");");
            });
            AppendLine();
        }

        // 3b. Rate limit — fixed window, per process (delay past-limit messages to the next window)
        if (_model.HasRateLimit)
        {
            var windowTicks = $"(long)({_model.RatePeriodSeconds} * Stopwatch.Frequency)";
            Comment($"Rate limit: {_model.RatePermitsPerPeriod}/{_model.RatePeriodSeconds}s fixed window");
            AppendLine("while (true)");
            Block(() =>
            {
                AppendLine("var __now = Stopwatch.GetTimestamp();");
                AppendLine("var __windowStart = Volatile.Read(ref __rateWindowStartTicks);");
                AppendLine($"if (__now - __windowStart >= {windowTicks})");
                Block(() =>
                {
                    Comment("Window elapsed — the winner of the CAS resets the counter");
                    AppendLine("if (Interlocked.CompareExchange(ref __rateWindowStartTicks, __now, __windowStart) == __windowStart)");
                    AppendLine("    Volatile.Write(ref __rateWindowCount, 0);");
                    AppendLine("continue;");
                });
                AppendLine($"if (Interlocked.Increment(ref __rateWindowCount) <= {_model.RatePermitsPerPeriod})");
                AppendLine("    break;");
                Comment("Over the limit — wait out the rest of the window");
                AppendLine($"var __waitTicks = {windowTicks} - (__now - __windowStart);");
                AppendLine("if (__waitTicks > 0)");
                AppendLine("    await global::System.Threading.Tasks.Task.Delay(global::System.TimeSpan.FromSeconds(__waitTicks / (double)Stopwatch.Frequency), ct).ConfigureAwait(false);");
            });
            AppendLine();
        }

        // 3c. Concurrency gate — bounded parallel executions, released in finally
        if (_model.HasConcurrencyLimit)
        {
            AppendLine("await __concurrencyGate.WaitAsync(ct).ConfigureAwait(false);");
            AppendLine("try");
            AppendLine("{");
            IncreaseIndent();
        }

        AppendLine("var stopwatch = Stopwatch.StartNew();");
        AppendLine($"LogHandlerStarted(\"{messageName}\", \"{_model.TypeName}\", context.MessageId);");
        AppendLine();

        // 4. Retry loop or direct invocation
        if (_model.HasRetry)
            RenderRetryLoop(messageName);
        else
            RenderTryCatchInvocation(messageName);

        // 5. Metrics + completion log
        AppendLine();
        AppendLine("stopwatch.Stop();");
        AppendLine($"MessagingDiagnostics.HandlerDuration.Record(stopwatch.Elapsed.TotalMilliseconds,");
        IncreaseIndent();
        AppendLine($"new KeyValuePair<string, object?>(\"handler.name\", \"{_model.TypeName}\"));");
        DecreaseIndent();
        AppendLine();
        AppendLine($"LogHandlerCompleted(\"{messageName}\", \"{_model.TypeName}\", stopwatch.Elapsed.TotalMilliseconds);");
        AppendLine("activity?.SetStatus(ActivityStatusCode.Ok);");

        if (_model.HasConcurrencyLimit)
        {
            DecreaseIndent();
            AppendLine("}");
            AppendLine("finally");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("__concurrencyGate.Release();");
            DecreaseIndent();
            AppendLine("}");
        }
    }

    // ── Retry loop ──

    private void RenderRetryLoop(string messageName)
    {
        var maxAttempts = _model.RetryMaxAttempts;
        var baseDelay = _model.RetryBaseDelayMs;
        // ⚠️ Raw numbers, because the generator cannot name the runtime enum. They are mirrored in
        // BackoffStrategyValues, which is the only place they are written down on this side.
        var strategy = _model.RetryStrategy;

        // The largest shift this base delay survives in int arithmetic. Without it,
        // `baseDelay * (1 << __attempt)` goes negative — past the twenty-first attempt for a one-second
        // base, past the thirty-first for any — and Task.Delay throws instead of waiting, failing the
        // handler for a reason that has nothing to do with the message. The bound is computed here
        // because baseDelay is a compile-time constant, so the generated code stays a single shift.
        var maxShift = MaxSafeShift(baseDelay);

        AppendLine($"for (var __attempt = 0; __attempt <= {maxAttempts}; __attempt++)");
        Block(() =>
        {
            AppendLine("try");
            Block(() =>
            {
                RenderHandlerInvocation();

                // Record success for circuit breaker
                if (_model.HasCircuitBreaker)
                    AppendLine("CircuitState.RecordSuccess();");

                AppendLine("break; // success — exit retry loop");
            });
            AppendLine($"catch (Exception ex) when (__attempt < {maxAttempts} && ex is not OperationCanceledException)");
            Block(() =>
            {
                // Record failure for circuit breaker
                if (_model.HasCircuitBreaker)
                    AppendLine("CircuitState.RecordFailure();");

                // Compute delay based on strategy
                switch (strategy)
                {
                    case BackoffStrategyValues.Fixed:
                        AppendLine($"var __delay = {baseDelay};");
                        break;
                    // Unspecified means the declaration did not say, and this engine's answer is
                    // plain exponential — the same one the transform substitutes when it reads.
                    case BackoffStrategyValues.Unspecified:
                    case BackoffStrategyValues.Exponential:
                        AppendLine($"var __delay = global::System.Math.Min({baseDelay} * (1 << global::System.Math.Min(__attempt, {maxShift})), {RetryDelayLimits.MaxDelayMilliseconds});");
                        break;
                    default: // ExponentialWithJitter
                        // Use a crypto-strong RNG for backoff jitter so retry scheduling isn't
                        // predictable from an attacker observing timing. System.Random.Shared has
                        // a deterministic internal state and leaks side-channel signal.
                        AppendLine($"var __delay = global::System.Math.Min({baseDelay} * (1 << global::System.Math.Min(__attempt, {maxShift})) + global::System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, {Math.Max(baseDelay, 1)}), {RetryDelayLimits.MaxDelayMilliseconds});");
                        break;
                }

                AppendLine($"LogRetryAttempt(\"{messageName}\", \"{_model.TypeName}\", __attempt + 1, __delay, ex);");
                AppendLine("MessagingDiagnostics.RetryAttempts.Add(1);");
                AppendLine("context = context.ForRetry();");
                AppendLine("await global::System.Threading.Tasks.Task.Delay(__delay, ct).ConfigureAwait(false);");
            });
            // Final attempt — release claim, optionally redeliver, else propagate
            AppendLine($"catch (Exception ex) when (__attempt >= {maxAttempts} && ex is not OperationCanceledException)");
            Block(() =>
            {
                if (_model.HasCircuitBreaker)
                    AppendLine("CircuitState.RecordFailure();");

                AppendLine($"MessagingDiagnostics.HandlerFailures.Add(1,");
                IncreaseIndent();
                AppendLine($"new KeyValuePair<string, object?>(\"message.type\", \"{messageName}\"),");
                AppendLine($"new KeyValuePair<string, object?>(\"handler.name\", \"{_model.TypeName}\"));");
                DecreaseIndent();
                AppendLine();
                AppendLine($"LogHandlerFailed(\"{messageName}\", \"{_model.TypeName}\", __attempt, ex);");
                RenderFinalFailure(messageName);
            });
        });
    }

    /// <summary>
    ///     The largest shift <paramref name="baseDelayMs" /> survives without overflowing an int.
    /// </summary>
    /// <remarks>
    ///     The delay saturates at that shift instead of wrapping negative. ⚠️ Saturating is not the same
    ///     as capping: this stops the arithmetic from breaking, and says nothing about how long a
    ///     handler ought to wait. The job engine truncates its own backoff at thirty minutes; whether
    ///     the message engine should do the same is a question about redelivery, not about ints.
    /// </remarks>
    private static int MaxSafeShift(int baseDelayMs)
    {
        // A shift of 31 or more on an int is not a bigger number, it is a different one.
        if (baseDelayMs <= 0)
            return 30;

        var shift = 0;
        long value = baseDelayMs;
        while (shift < 30 && value * 2 <= int.MaxValue)
        {
            value *= 2;
            shift++;
        }

        return shift;
    }

    // ── Final failure epilogue (shared by retry / no-retry paths) ──
    // 1. Release this handler's idempotency claim: the claim was taken at entry, and without the
    //    release a broker redelivery (or the scheduled redelivery below) would be skipped as a
    //    duplicate — the failure would silently become "processed".
    // 2. With [Redelivery]: re-schedule through IMessageScheduler (persistent, survives process
    //    death) keeping the ORIGINAL MessageId so sibling handlers dedupe, with RetryCount as the
    //    redelivery counter. Requires both scheduler and idempotency store; falls through to
    //    throw (→ transport dead-letter) when exhausted or not available.

    private void RenderFinalFailure(string messageName)
    {
        AppendLine("if (_idempotencyStore is not null)");
        AppendLine($"    await _idempotencyStore.RemoveAsync(context.MessageId + \":{HandlerFqn}\", CancellationToken.None).ConfigureAwait(false);");

        if (_model.HasRedelivery)
        {
            AppendLine();
            AppendLine($"if (_scheduler is not null && _idempotencyStore is not null && __deliveryCount < {_model.RedeliveryMaxAttempts})");
            Block(() =>
            {
                AppendLine($"var __redeliveryDelay = global::System.TimeSpan.FromSeconds({_model.RedeliveryBaseDelaySeconds}L << global::System.Math.Min(__deliveryCount, 10));");
                AppendLine("await _scheduler.ScheduleAsync(message, __redeliveryDelay, context with { RetryCount = __deliveryCount + 1 }, ct).ConfigureAwait(false);");
                AppendLine($"LogRedeliveryScheduled(\"{messageName}\", \"{_model.TypeName}\", __deliveryCount + 1, __redeliveryDelay);");
                AppendLine("activity?.SetStatus(ActivityStatusCode.Error, ex.Message);");
                AppendLine("return;");
            });
        }

        AppendLine("activity?.SetStatus(ActivityStatusCode.Error, ex.Message);");
        AppendLine("throw;");
    }

    // ── Direct invocation (no retry) ──

    private void RenderTryCatchInvocation(string messageName)
    {
        AppendLine("try");
        Block(() =>
        {
            RenderHandlerInvocation();

            if (_model.HasCircuitBreaker)
                AppendLine("CircuitState.RecordSuccess();");
        });
        AppendLine("catch (Exception ex) when (ex is not OperationCanceledException)");
        Block(() =>
        {
            if (_model.HasCircuitBreaker)
                AppendLine("CircuitState.RecordFailure();");

            AppendLine($"MessagingDiagnostics.HandlerFailures.Add(1,");
            IncreaseIndent();
            AppendLine($"new KeyValuePair<string, object?>(\"message.type\", \"{messageName}\"),");
            AppendLine($"new KeyValuePair<string, object?>(\"handler.name\", \"{_model.TypeName}\"));");
            DecreaseIndent();
            AppendLine();
            AppendLine($"LogHandlerFailed(\"{messageName}\", \"{_model.TypeName}\", context.RetryCount, ex);");
            RenderFinalFailure(messageName);
        });
    }

    // ── Handler invocation (with optional timeout + auth bypass) ──

    private void RenderHandlerInvocation()
    {
        if (_model.FeatureHasAuthorization)
        {
            AppendLine("var scope = _callContext?.EnterInternalCall();");
            AppendLine("try");
            Block(() => RenderHandlerCall());
            AppendLine("finally");
            Block(() => AppendLine("scope?.Dispose();"));
        }
        else
        {
            RenderHandlerCall();
        }
    }

    private void RenderHandlerCall()
    {
        // Build the middleware chain around the handler. The
        // innermost delegate invokes the handler; each middleware wraps the
        // previous one so that Order=-100 runs outermost, Order=100 runs
        // closest to the handler. When no middleware is registered, the
        // chain collapses to a direct handler call with no allocation tax
        // beyond the single inner delegate.
        if (_model.HasTimeout)
        {
            AppendLine("using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);");
            AppendLine($"cts.CancelAfter(global::System.TimeSpan.FromSeconds({_model.TimeoutSeconds}));");
            AppendLine("var handlerCt = cts.Token;");
        }
        else
        {
            AppendLine("var handlerCt = ct;");
        }

        AppendLine("global::Pragmatic.Messaging.MessageHandlerDelegate next = () => _handler.HandleAsync(message, context, handlerCt);");
        AppendLine("for (var __i = _middlewares.Count - 1; __i >= 0; __i--)");
        Block(() =>
        {
            AppendLine("var __mw = _middlewares[__i];");
            AppendLine("var __inner = next;");
            AppendLine("next = () => __mw.InvokeAsync(message, context, __inner, handlerCt);");
        });
        AppendLine("await next().ConfigureAwait(false);");
    }

    // ── Log methods ──

    private static readonly (string Type, string Name)[] MessageAndHandler =
        [("string", "messageName"), ("string", "handlerName")];

    private void RenderLoggerMessages()
    {
        RenderLogMethod("LogHandlerStarted", "Debug",
            "Starting handler {HandlerName} for message {MessageName} (id: {MessageId})",
            [..MessageAndHandler, ("string", "messageId")],
            ["handlerName", "messageName", "messageId"]);
        AppendLine();

        RenderLogMethod("LogHandlerCompleted", "Debug",
            "Handler {HandlerName} completed for message {MessageName} in {DurationMs:F1}ms",
            [..MessageAndHandler, ("double", "durationMs")],
            ["handlerName", "messageName", "durationMs"]);
        AppendLine();

        RenderLogMethod("LogHandlerFailed", "Error",
            "Handler {HandlerName} failed for message {MessageName} (retry {RetryCount})",
            [..MessageAndHandler, ("int", "retryCount")],
            ["handlerName", "messageName", "retryCount"],
            exception: "ex");
        AppendLine();

        // Idempotency
        RenderLogMethod("LogDuplicateSkipped", "Debug",
            "Duplicate message skipped: {MessageName} handler {HandlerName} (id: {MessageId})",
            [..MessageAndHandler, ("string", "messageId")],
            ["messageName", "handlerName", "messageId"]);

        // Retry
        if (_model.HasRetry)
        {
            AppendLine();
            RenderLogMethod("LogRetryAttempt", "Warning",
                "Retrying handler {HandlerName} for {MessageName}: attempt {Attempt}, delay {DelayMs}ms",
                [..MessageAndHandler, ("int", "attempt"), ("int", "delayMs")],
                ["handlerName", "messageName", "attempt", "delayMs"],
                exception: "ex");
        }

        // Circuit breaker
        if (_model.HasCircuitBreaker)
        {
            AppendLine();
            RenderLogMethod("LogCircuitOpen", "Warning",
                "Circuit breaker OPEN for handler {HandlerName}, rejecting {MessageName}",
                MessageAndHandler,
                ["handlerName", "messageName"]);
        }

        // Redelivery
        if (_model.HasRedelivery)
        {
            AppendLine();
            RenderLogMethod("LogRedeliveryScheduled", "Warning",
                "Scheduled persistent redelivery {Redelivery} of {MessageName} for handler {HandlerName} in {Delay}",
                [..MessageAndHandler, ("int", "redelivery"), ("global::System.TimeSpan", "delay")],
                ["redelivery", "messageName", "handlerName", "delay"]);
        }
    }
}
