using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Logging.Templates;
using Pragmatic.SourceGenerator.Features.Logging.Transforms;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates a transactional invoker for [CompositeAction] DomainActions.
///     The invoker executes all mutation steps without saving, then commits atomically.
/// </summary>
/// <remarks>
///     Its one log line is a Pragmatic call site written into the invoker in this pass.
/// </remarks>
internal sealed class CompositeActionInvokerTemplate : LogCallSiteTemplateBase
{
    private readonly CompositeActionModel _model;

    public CompositeActionInvokerTemplate(CompositeActionModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} CompositeAction from {_model.Namespace}";
    protected override string? TriggerInfo => $"[CompositeAction] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "CompositeInvoker", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, HasSteps: true };

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Nested inside partial action class (1 nested class per file)
        Class(_model.TypeName, () =>
        {
            XmlSummary(
                $"Generated composite invoker for <see cref=\"{_model.TypeName}\"/>. " +
                "Executes all mutation steps within a single transaction.");

            Class("CompositeInvoker", RenderBody,
                accessModifier: AccessModifier.Public,
                modifiers: new ClassModifiers { Sealed = true });

            RenderExecuteOverride();
        },
        accessModifier: ParseAccessibility(_model.Accessibility),
        modifiers: new ClassModifiers { Partial = true });
    }

    /// <summary>
    ///     The <c>Execute</c> the base class demands and a composite has no use for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A composite's body <b>is</b> its steps: the generated <c>CompositeInvoker</c> runs them,
    ///         and this override is never called. Before, the author had to write it themselves —
    ///         <c>=&gt; Task.FromResult(Success())</c>, a method in their own file stating that the
    ///         operation does nothing and succeeds, which is false about the one thing a reader checks
    ///         first.
    ///     </para>
    ///     <para>
    ///         Skipped when the author wrote one anyway: the two would collide, and someone who writes
    ///         a body on a composite is saying something the generator should not overwrite.
    ///     </para>
    /// </remarks>
    private void RenderExecuteOverride()
    {
        if (_model.DeclaresExecute)
            return;

        var returnType = _model.IsVoid
            ? "global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>"
            : $"global::Pragmatic.Result.Result<{_model.ReturnTypeName}, global::Pragmatic.Result.IError>";

        AppendLine();
        XmlSummary(
            "Never called: the steps are the body, and the generated CompositeInvoker runs them. "
            + "It exists because the base class declares Execute abstract.");
        AppendLine(
            $"public override global::System.Threading.Tasks.Task<{returnType}> Execute("
            + "global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        const string message =
            "A composite action is executed by its generated CompositeInvoker, not through Execute.";

        AppendLine(
            "=> throw new global::System.NotSupportedException(\"" + message + "\");");
        DecreaseIndent();
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            _ => AccessModifier.Public
        };
    }

    private void RenderBody()
    {
        // Fields: UoW + service provider (for post-commit event dispatch / cache) + mutation invokers
        Field("_unitOfWork", UnitOfWorkTypeName, AccessModifier.Private, isReadOnly: true);
        Field("_serviceProvider", "global::System.IServiceProvider", AccessModifier.Private, isReadOnly: true);

        foreach (var step in _model.Steps)
        {
            var fieldName = $"_{TemplateHelpers.ToCamelCase(step.PropertyName)}Invoker";
            Field(fieldName, step.InvokerFullTypeName, AccessModifier.Private, isReadOnly: true);
        }

        AppendLine();

        RenderConstructor();
        AppendLine();
        RenderExecuteAsync();
        AppendLine();

        var postCommitFailed = GeneratedLogCallSite.Create(
            "LogPostCommitFailed", "private static", "logger", "Error",
            "Composite action committed but a post-commit side effect (event dispatch / cache invalidation) threw — reported as success; the side effect failed",
            [("global::Microsoft.Extensions.Logging.ILogger", "logger")],
            exception: "ex");
        RenderCallSite(postCommitFailed, StateName(postCommitFailed, new System.Collections.Generic.HashSet<string>()));
    }

    private void RenderConstructor()
    {
        var ctorParams = new List<string>();

        if (_model.BelongsToTypeName is not null)
            ctorParams.Add(
                $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({_model.BelongsToTypeName}))] {UnitOfWorkTypeName} unitOfWork");
        else
            ctorParams.Add($"{UnitOfWorkTypeName} unitOfWork");

        foreach (var step in _model.Steps)
            ctorParams.Add($"{step.InvokerFullTypeName} {TemplateHelpers.ToCamelCase(step.PropertyName)}Invoker");

        ctorParams.Add("global::System.IServiceProvider serviceProvider");

        var paramString = string.Join(",\n        ", ctorParams);

        AppendLine("public CompositeInvoker(");
        AppendLine($"        {paramString})");
        Block(() =>
        {
            AppendLine("_unitOfWork = unitOfWork;");
            AppendLine("_serviceProvider = serviceProvider;");

            foreach (var step in _model.Steps)
            {
                var fieldName = $"_{TemplateHelpers.ToCamelCase(step.PropertyName)}Invoker";
                var paramName = $"{TemplateHelpers.ToCamelCase(step.PropertyName)}Invoker";
                AppendLine($"{fieldName} = {paramName};");
            }
        });
    }

    private void RenderExecuteAsync()
    {
        XmlSummary(
            $"Executes all mutations in <see cref=\"{_model.TypeName}\"/> within a single transaction.");

        var returnType = _model.IsVoid
            ? "global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>"
            : $"global::Pragmatic.Result.Result<{_model.ReturnTypeName ?? "object"}, global::Pragmatic.Result.IError>";

        var signature = $"{_model.FullTypeName} action, global::System.Threading.CancellationToken ct";
        if (_model.IsTransactional)
        {
            // Inside the unit of work's execution strategy: a strategy that retries refuses the transaction
            // below when it is opened outside it. A transient failure before the commit runs the
            // steps again from a cleared change tracker; what follows the commit is caught and runs once.
            AppendLine(
                $"public global::System.Threading.Tasks.Task<{returnType}> ExecuteAsync({signature} = default)");
            IncreaseIndent();
            AppendLine("=> _unitOfWork.ExecuteAsync(token => ExecuteOnceAsync(action, token), ct);");
            DecreaseIndent();
            AppendLine();
            AppendLine(
                $"private async global::System.Threading.Tasks.Task<{returnType}> ExecuteOnceAsync({signature})");
        }
        else
        {
            AppendLine(
                $"public async global::System.Threading.Tasks.Task<{returnType}> ExecuteAsync({signature} = default)");
        }

        Block(() =>
        {
            // ⚠️ Only where the composite declares that it answers for its steps' permissions.
            // `IsInternalCall` is read only by the three authorization filters, so entering it means
            // exactly «do not ask for permissions»: done unconditionally, a [RequirePermission] on a
            // mutation would stop applying as soon as someone used it as a step. The default is the
            // nested children's — the strict rule — and absorbing is declared, so it is a visible
            // choice rather than a property of the mechanism. The comment emitted into the generated
            // file says what the branch it sits in does.
            if (_model.AbsorbsChildPermissions)
            {
                Comment("[AbsorbsChildPermissions]: the composite answers for its steps. Its own");
                Comment("[RequirePermission]/policy (if any) is enforced before ExecuteActionAsync, and the steps");
                Comment("run as internal calls, so their own permission checks are not repeated here.");
                AppendLine("var __callContext = _serviceProvider.GetService<global::Pragmatic.Actions.Pipeline.ActionCallContext>();");
                AppendLine("using var __internalScope = __callContext?.EnterInternalCall();");
            }
            else
            {
                Comment("Each step keeps its own permission check: the composite's [RequirePermission]/policy");
                Comment("(if any) is enforced before ExecuteActionAsync, and a step that declares one is asked");
                Comment("for it again as it runs. [AbsorbsChildPermissions] on the composite is what turns that off.");
            }
            AppendLine();

            if (_model.IsTransactional)
            {
                // [Transactional] buys one thing a plain composite cannot give: a step that reads what
                // an earlier step wrote. The claim below therefore does NOT defer the saves — each step
                // writes — and the transaction is what makes a later failure undo them.
                Comment("One transaction around the steps, because a step reads what the previous wrote.");
                AppendLine(
                    "await using var __transaction = await _unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);");
                AppendLine(
                    "using var __commitOwnership = global::Pragmatic.Actions.Commit.CommitScope.Claim(" +
                    "_unitOfWork, global::Pragmatic.Actions.Commit.CommitMode.Once, transactional: true);");
            }
            else
            {
                Comment("Claim the commit for this boundary before the steps run: each step then sees that");
                Comment("an outer invoker owns its unit of work, stages its writes and defers its events.");
                AppendLine(
                    "using var __commitOwnership = global::Pragmatic.Actions.Commit.CommitScope.Claim(" +
                    "_unitOfWork, global::Pragmatic.Actions.Commit.CommitMode.Once);");
            }

            AppendLine();

            Comment("Execute each mutation without saving");
            foreach (var step in _model.Steps)
            {
                var fieldName = $"_{TemplateHelpers.ToCamelCase(step.PropertyName)}Invoker";
                var varName = $"{TemplateHelpers.ToCamelCase(step.PropertyName)}Result";

                // InvokeAsync for every kind of step: the claim above already means "do not commit",
                // so a mutation needs no special entry point and an action — which never had one — can
                // be a step at all.
                AppendLine(
                    $"var {varName} = await {fieldName}.InvokeAsync(action.{step.PropertyName}, ct).ConfigureAwait(false);");

                var failure = _model.IsVoid
                    ? $"global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Failure({varName}.Error)"
                    : $"{returnType}.Failure({varName}.Error)";

                AppendLine($"if ({varName}.IsFailure)");
                if (_model.IsTransactional)
                {
                    // Rolled back explicitly rather than left to the dispose: the steps have already
                    // written, and "the disposer will handle it" is not a guarantee to read off a
                    // generated early return.
                    Block(() =>
                    {
                        AppendLine("await __transaction.RollbackAsync(ct).ConfigureAwait(false);");
                        AppendLine($"return {failure};");
                    });
                }
                else
                {
                    AppendLine($"    return {failure};");
                }

                AppendLine();
            }

            Comment("All mutations succeeded — commit atomically");
            AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
            if (_model.IsTransactional)
                AppendLine("await __transaction.CommitAsync(ct).ConfigureAwait(false);");

            AppendLine();

            // The commit already happened, so a failure in the post-commit flush (event dispatch /
            // cache invalidation) must NOT surface as a thrown failure — that would make the caller retry
            // and double-run a committed composite. Isolate it and log at Error instead.
            AppendLine("try");
            Block(RenderPostCommitFlush);
            AppendLine("catch (global::System.Exception __postCommitEx)");
            Block(() =>
            {
                AppendLine(
                    "var __loggerFactory = _serviceProvider.GetService<global::Microsoft.Extensions.Logging.ILoggerFactory>();");
                AppendLine("if (__loggerFactory is not null)");
                AppendLine("    LogPostCommitFailed(__loggerFactory.CreateLogger(\"Pragmatic.Actions.CompositeAction\"), __postCommitEx);");
            });
            AppendLine();

            if (_model.IsVoid)
            {
                AppendLine(
                    "return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Success();");
            }
            else
            {
                // Return the result of the last step
                var lastStep = _model.Steps[_model.Steps.Length - 1];
                var lastVar = $"{TemplateHelpers.ToCamelCase(lastStep.PropertyName)}Result";
                AppendLine($"return action.GetResult({lastVar}.Value);");
            }
        });
    }

    /// <summary>
    ///     Post-commit side effects: the steps ran in batch mode, so SaveChanges/event-dispatch/cache
    ///     were deferred. Dispatch the batch's events once (after the single commit) and invalidate the
    ///     cache for any step mutation that is an <c>ICacheInvalidator</c>. Mirrors MutationInvoker.Lifecycle.
    /// </summary>
    private void RenderPostCommitFlush()
    {
        Comment("Post-commit: dispatch deferred domain events once, then invalidate caches.");
        AppendLine(
            "var __dispatcher = _serviceProvider.GetService<global::Pragmatic.Events.IDomainEventDispatcher>();");
        AppendLine("var __batch = __commitOwnership.Batch;");
        AppendLine("if (__dispatcher is not null && __batch is not null && __batch.DeferredEvents.Count > 0)");
        AppendLine(
            "    await __dispatcher.DispatchAsync(global::System.Linq.Enumerable.Cast<global::Pragmatic.Events.IDomainEvent>(__batch.DeferredEvents), ct).ConfigureAwait(false);");
        AppendLine();

        AppendLine(
            "var __cacheStack = _serviceProvider.GetService<global::Pragmatic.Caching.ICacheStack>();");
        AppendLine("if (__cacheStack is not null)");
        Block(() =>
        {
            for (var i = 0; i < _model.Steps.Length; i++)
            {
                var step = _model.Steps[i];
                AppendLine(
                    $"if (action.{step.PropertyName} is global::Pragmatic.Caching.ICacheInvalidator __inv{i})");
                AppendLine(
                    $"    await __inv{i}.InvalidateAsync(__cacheStack, ct).ConfigureAwait(false);");
            }
        });
        AppendLine();
    }

    private static string UnitOfWorkTypeName => "global::Pragmatic.Persistence.Repository.IUnitOfWork";
}
