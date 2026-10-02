using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed partial class InvokerTemplate : CSharpTemplate
{
    private void RenderExecuteActionAsync()
    {
        var summary = _model.HasFilterOverrides
            ? "Wraps action execution with filter override scopes."
            : _model.IsComposite
                ? "Wraps action execution in a BatchContext for atomic commit."
                : _model.HasVersioning
                    ? "Dispatches to the correct versioned Execute method based on TargetVersion."
                    : _model.HasResilience
                        ? $"Wraps action execution with resilience policy \"{_model.Resilience!.PolicyName}\"."
                        : "Executes the action.";

        XmlSummary(summary);

        if (_model.IsVoid)
        {
            AppendLine($"protected override async global::System.Threading.Tasks.Task<global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>> ExecuteActionAsync({_model.FullTypeName} action, global::System.Threading.CancellationToken ct)");
        }
        else
        {
            var returnType = _model.ReturnTypeName ?? "object";
            AppendLine($"protected override async global::System.Threading.Tasks.Task<global::Pragmatic.Result.Result<{returnType}, global::Pragmatic.Result.IError>> ExecuteActionAsync({_model.FullTypeName} action, global::System.Threading.CancellationToken ct)");
        }

        Block(() =>
        {
            // [AbsorbsChildPermissions]: everything this action invokes runs as an internal call, so
            // the permissions of what it calls are not asked again. Outermost, and before delegation,
            // because it must hold for every call the body makes.
            // ⚠️ Its own permission is unaffected: that check runs before ExecuteActionAsync.
            if (_model.AbsorbsChildPermissions)
            {
                AppendLine("var __absorbContext = global::Microsoft.Extensions.DependencyInjection"
                    + ".ServiceProviderServiceExtensions"
                    + ".GetService<global::Pragmatic.Actions.Pipeline.ActionCallContext>(ServiceProvider);");
                AppendLine("using var __absorbScope = __absorbContext?.EnterInternalCall();");
                AppendLine();
            }

            // Filter overrides wrap everything
            if (_model.HasFilterOverrides)
                RenderFilterOverrideScopes();

            if (_model.IsComposite)
                RenderCompositeExecution();
            else if (_model.HasResilience)
                RenderResilienceWrappedExecution();
            else if (_model.HasVersioning)
                RenderVersionedDispatch();
            else
                AppendLine("return await base.ExecuteActionAsync(action, ct).ConfigureAwait(false);");
        });
    }

    private void RenderCompositeExecution()
    {
        // Declarative mutation-step composite: delegate to the generated CompositeInvoker, which owns
        // the single batch, the one commit, and the post-commit event/cache flush. Runs inside this
        // invoker so the filter/permission pipeline (BeforeExecute/AfterExecute) is preserved.
        if (_model.HasCompositeSteps)
        {
            AppendLine("return await _compositeInvoker.ExecuteAsync(action, ct).ConfigureAwait(false);");
            return;
        }

        AppendLine("using var batch = new global::Pragmatic.Persistence.Lifecycle.BatchContext();");

        if (_model.HasResilience)
        {
            RenderResilienceWrappedExecution();
        }
        else if (_model.HasVersioning)
        {
            RenderVersionedDispatch();
        }
        else
        {
            AppendLine("return await action.Execute(ct).ConfigureAwait(false);");
        }
    }

    private void RenderVersionedDispatch()
    {
        AppendLine("return action.TargetVersion switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var version in _model.Versions)
        {
            if (version.Major == 1 && version.Minor == 0)
                continue;
            AppendLine($"({version.Major}, {version.Minor}) => await action.{version.MethodName}(ct).ConfigureAwait(false),");
        }

        AppendLine("_ => await action.Execute(ct).ConfigureAwait(false)");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderResilienceWrappedExecution()
    {
        var policyName = _model.Resilience!.PolicyName;
        var actionName = _model.TypeName;

        AppendLine($"var pipeline = _resiliencePipelineProvider.GetPipeline(\"{StringHelper.CSharpLiteral(policyName)}\");");

        // Map resilience exceptions (circuit-broken, timeout, retry/hedging-exhausted, bulkhead/rate-limit
        // rejected) into the action's typed Result so the caller gets the correct error + HTTP status
        // instead of a raw exception escaping to a generic 500. Non-resilience exceptions still propagate.
        AppendLine("try");
        Block(() =>
        {
            AppendLine("return await pipeline.ExecuteAsync(");
            IncreaseIndent();

            if (_model.HasVersioning)
            {
                AppendLine("async (ctx, innerCt) =>");
                Block(() =>
                {
                    RenderVersionedDispatchInner();
                }, modifier: ",");
            }
            else
            {
                AppendLine("(ctx, innerCt) => action.Execute(innerCt),");
            }

            AppendLine($"new global::Pragmatic.Resilience.ResilienceContext {{ OperationName = \"{actionName}\" }},");
            AppendLine("ct).ConfigureAwait(false);");
            DecreaseIndent();
        });

        AppendLine(
            "catch (global::System.Exception __resilienceEx) when (global::Pragmatic.Resilience.Bridge.ResilienceResultBridge.TryMapToError(__resilienceEx, out var __resilienceError))");
        Block(() =>
        {
            if (_model.IsVoid)
            {
                AppendLine(
                    "return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Failure(__resilienceError);");
            }
            else
            {
                var returnType = _model.ReturnTypeName ?? "object";
                AppendLine(
                    $"return global::Pragmatic.Result.Result<{returnType}, global::Pragmatic.Result.IError>.Failure(__resilienceError);");
            }
        });
    }

    private void RenderVersionedDispatchInner()
    {
        AppendLine("return action.TargetVersion switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var version in _model.Versions)
        {
            if (version.Major == 1 && version.Minor == 0)
                continue;
            AppendLine($"({version.Major}, {version.Minor}) => await action.{version.MethodName}(innerCt).ConfigureAwait(false),");
        }

        AppendLine("_ => await action.Execute(innerCt).ConfigureAwait(false)");
        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>
    ///     Opens the delegation <c>[StartsDelegation]</c> declares, for the whole invocation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Generated rather than left to the author because the action body runs after
    ///         authorization has already decided: a hand-written <c>ActAs</c> there would authorize as
    ///         the caller and execute as the subject. And because a <c>using</c> forgotten on an
    ///         <c>AsyncLocal</c> leaks into the next request on the same pooled thread, which nothing
    ///         would report.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>On <c>BeginInvocationScope</c> and not on <c>ExecuteActionAsync</c></b>, because the row is attributed at <c>SaveChanges</c>, and the
    ///         pipeline saves after the body returns. Opened around the body alone, the scope had
    ///         already disposed by then and the ownership and audit stamps read the caller — the
    ///         declaration bought nothing on the one effect its own documentation names first.
    ///         The pipeline holds this one from before the <c>[LoadEntity]</c> loads until after the
    ///         post-commit side effects, so the row filters see the composed authority too.
    ///     </para>
    /// </remarks>
    private void RenderBeginInvocationScope()
    {
        var scope = _model.DelegationScope!;
        var purpose = scope.Purpose is null ? "null" : $"\"{Escape(scope.Purpose)}\"";

        XmlSummary("Opens the declared delegation for the whole invocation, saves included.");

        AppendLine(
            "protected override global::System.IDisposable? BeginInvocationScope("
            + $"{_model.FullTypeName} action)");
        IncreaseIndent();
        AppendLine(
            $"=> _delegationService?.ActAs(action.{scope.SubjectPropertyName}, " +
            $"purpose: {purpose}, " +
            $"policy: (global::Pragmatic.Identity.DelegationPolicy){scope.Policy});");
        DecreaseIndent();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private void RenderFilterOverrideScopes()
    {
        foreach (var line in FilterOverrideEmitter.ScopeLines(_model.FilterOverrides!, "_filterToggle?"))
            AppendLine(line);
    }
}
