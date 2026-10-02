namespace Pragmatic.Resilience.Attributes;

/// <summary>
/// Marks a DomainAction or Mutation to be wrapped with a named resilience policy.
/// The source generator injects IResiliencePipelineProvider and wraps ExecuteActionAsync
/// with the specified pipeline. Only exceptions trigger resilience (retry, circuit breaker);
/// Result failures are business errors and pass through unchanged.
/// </summary>
/// <example>
/// <code>
/// [DomainAction]
/// [ResiliencePolicy("external-api")]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt;
/// {
///     public Task&lt;Result&lt;OrderId, IError&gt;&gt; Execute(CancellationToken ct) => ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ResiliencePolicyAttribute : Attribute
{
    /// <summary>The name of the resilience policy to apply (matches configuration key).</summary>
    public string PolicyName { get; }

    /// <summary>
    /// Creates a new resilience policy attribute.
    /// </summary>
    /// <param name="policyName">
    /// The name of the resilience policy (matches a key in Resilience:Policies configuration
    /// or a fluently registered policy name).
    /// </param>
    public ResiliencePolicyAttribute(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        PolicyName = policyName;
    }
}
