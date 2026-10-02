namespace Pragmatic.Resilience;

/// <summary>
///     An operation that declares <c>[ResiliencePolicy(PolicyName)]</c>, registered by the module's generated
///     code so the host can check at startup that the name is defined.
/// </summary>
/// <param name="PolicyName">The name the attribute gives.</param>
/// <param name="Operation">The fully qualified name of the operation that declares it.</param>
public sealed record DeclaredResiliencePolicy(string PolicyName, string Operation);
