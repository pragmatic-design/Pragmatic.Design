using Pragmatic.Resilience.Pipeline;

namespace Pragmatic.Resilience;

/// <summary>
/// Resolves named resilience pipelines from configuration + fluent registration.
/// Read-only view; for registration use <see cref="IResiliencePipelineRegistry"/>.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IResiliencePipelineProvider
{
    /// <summary>Get pipeline by name. Returns PassthroughPipeline if not found.</summary>
    IResiliencePipeline GetPipeline(string policyName);

    /// <summary>Get pipeline for a specific operation (action/endpoint name).</summary>
    IResiliencePipeline GetPipelineForOperation(string operationName);
}
