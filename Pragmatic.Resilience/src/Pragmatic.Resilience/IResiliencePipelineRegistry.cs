using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Pipeline;

namespace Pragmatic.Resilience;

/// <summary>
/// Mutation interface for registering and mapping resilience policies at startup.
/// Separated from <see cref="IResiliencePipelineProvider"/> to honour ISP:
/// consumers that only read pipelines should not be forced to implement registration logic.
/// </summary>
public interface IResiliencePipelineRegistry : IResiliencePipelineProvider
{
    /// <summary>Register a fluent policy override.</summary>
    void AddPolicy(string name, Func<ResiliencePipelineBuilder, ResiliencePipelineBuilder> configure);

    /// <summary>Register a policy options override.</summary>
    void AddPolicy(string name, ResiliencePolicyOptions options);

    /// <summary>Map an operation name to a policy name.</summary>
    void MapOperation(string operationName, string policyName);
}
