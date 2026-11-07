using System.Collections.Generic;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     One private helper per step with <c>[CompensateWith&lt;T&gt;]</c>: it builds the compensator from the saga's state and publishes it. The
///     construction is written out, from <see cref="Models.CompensationSeedModel" />: no JSON round trip and no reflective resolver (#532), so the
///     saga's assembly gets no IL2026/IL3050 from it.
/// </summary>
internal sealed partial class SagaOrchestratorTemplate
{
    private void RenderCompensationDispatchers(string sagaFqn)
    {
        var emitted = new HashSet<string>();
        foreach (var step in _model.Steps)
        {
            if (string.IsNullOrEmpty(step.CompensationActionFqn)) continue;
            if (!emitted.Add(step.MethodName)) continue;
            // The transform reads a seed for every [CompensateWith<T>]; a model built without one (a template test) has nothing to build from.
            var seed = step.CompensationSeed ?? new Models.CompensationSeedModel { CanConstruct = false };

            AppendLine();
            if (!seed.CanConstruct)
            {
                // Abstract, an interface, or no constructor System.Text.Json could have called either: nothing to publish.
                AppendLine($"/// <summary><see cref=\"{step.CompensationActionFqn}\"/> cannot be built from the saga state: the compensation is skipped.</summary>");
                AppendLine($"private global::System.Threading.Tasks.Task DispatchCompensation_{step.MethodName}({sagaFqn} instance, CancellationToken ct)");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"LogSagaCompensationSkipped(\"{_model.TypeName}\", \"{step.MethodName}\");");
                AppendLine("return global::System.Threading.Tasks.Task.CompletedTask;");
                DecreaseIndent();
                AppendLine("}");
                continue;
            }

            AppendLine($"/// <summary>Seeds <see cref=\"{step.CompensationActionFqn}\"/> from the saga state, member by name, and publishes it.</summary>");
            AppendLine($"private async global::System.Threading.Tasks.Task DispatchCompensation_{step.MethodName}({sagaFqn} instance, CancellationToken ct)");
            AppendLine("{");
            IncreaseIndent();
            var construction = $"new {step.CompensationActionFqn}({string.Join(", ", seed.Arguments)})";
            if (seed.Assignments.Length == 0)
                AppendLine($"var compensator = {construction};");
            else
            {
                AppendLine($"var compensator = {construction}");
                AppendLine("{");
                IncreaseIndent();
                foreach (var assignment in seed.Assignments)
                    AppendLine(assignment + ",");
                DecreaseIndent();
                AppendLine("};");
            }

            AppendLine("await _bus.PublishAsync(compensator, ct).ConfigureAwait(false);");
            AppendLine($"LogSagaCompensated(\"{_model.TypeName}\", \"{step.MethodName}\");");
            DecreaseIndent();
            AppendLine("}");
        }

        AppendLine();
    }
}
