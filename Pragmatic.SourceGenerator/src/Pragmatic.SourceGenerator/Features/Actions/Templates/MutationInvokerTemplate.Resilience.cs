using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     <c>[ResiliencePolicy]</c> on a mutation: the attempts run inside the named pipeline.
/// </summary>
internal sealed partial class MutationInvokerTemplate
{
    private const string ResilienceProviderTypeName = "global::Pragmatic.Resilience.IResiliencePipelineProvider";

    /// <summary>
    ///     Overrides <c>RunAttemptsAsync</c> with the pipeline the attribute names.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same shape as the domain-action invoker's wrapped execution, and for the same reason
    ///         the resilience exceptions become the operation's typed failure: a circuit that is open, a
    ///         timeout, retries exhausted answer with their error and status instead of a raw 500.
    ///     </para>
    ///     <para>
    ///         What is retried is the whole invocation, save included — the failure a retry exists for is
    ///         the one the database returns. Each attempt after the first starts from a unit of work that
    ///         has forgotten the previous one (<c>MutationInvoker.AttemptFor</c>).
    ///     </para>
    /// </remarks>
    private void RenderRunAttemptsUnderThePolicy()
    {
        var resultType =
            $"global::Pragmatic.Result.Result<{_model.EntityFullTypeName}, global::Pragmatic.Result.IError>";

        XmlSummary($"Runs the attempts inside resilience policy \"{_model.Resilience!.PolicyName}\".");
        AppendLine(
            $"protected override async global::System.Threading.Tasks.Task<{resultType}> RunAttemptsAsync(");
        AppendLine(
            $"    global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<{resultType}>> attempt,");
        AppendLine("    global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            AppendLine(
                $"var pipeline = _resiliencePipelineProvider.GetPipeline(\"{StringHelper.CSharpLiteral(_model.Resilience!.PolicyName)}\");");
            AppendLine("try");
            Block(() =>
            {
                AppendLine("return await pipeline.ExecuteAsync(");
                IncreaseIndent();
                AppendLine("(ctx, innerCt) => attempt(innerCt),");
                AppendLine(
                    $"new global::Pragmatic.Resilience.ResilienceContext {{ OperationName = \"{_model.TypeName}\" }},");
                AppendLine("ct).ConfigureAwait(false);");
                DecreaseIndent();
            });
            AppendLine(
                "catch (global::System.Exception __resilienceEx) when (global::Pragmatic.Resilience.Bridge.ResilienceResultBridge.TryMapToError(__resilienceEx, out var __resilienceError))");
            Block(() => AppendLine($"return {resultType}.Failure(__resilienceError);"));
        });
    }
}
