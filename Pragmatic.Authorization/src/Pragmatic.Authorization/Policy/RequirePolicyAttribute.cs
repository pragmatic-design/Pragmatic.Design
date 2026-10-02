namespace Pragmatic.Authorization.Policy;

/// <summary>
///     Requires the specified <see cref="ResourcePolicy" /> to be satisfied before
///     executing the action. Evaluated by <c>PolicyEvaluationFilter</c> at Order 210.
/// </summary>
/// <typeparam name="TPolicy">
///     The policy type. Must have a parameterless constructor.
///     The instance is created once and cached.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequirePolicyAttribute<TPolicy> : Attribute
    where TPolicy : ResourcePolicy, new();
