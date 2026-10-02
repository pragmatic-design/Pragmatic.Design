namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Stands in for <see cref="IPolicyRegistry" /> when the source generator produced none, and
///     refuses every question instead of answering "no policy".
/// </summary>
/// <remarks>
///     The L2 twin of <see cref="UnavailablePermissionRequirementRegistry" />, and it exists for the
///     same reason: a registry emitted for every assembly with actions means an absent one has
///     exactly one cause, and answering <see langword="null" /> to a question you cannot answer reads
///     as "no policy required" and lets the call through.
/// </remarks>
internal sealed class UnavailablePolicyRegistry : IPolicyRegistry
{
    public static readonly UnavailablePolicyRegistry Instance = new();

    public Pragmatic.Authorization.Policy.ResourcePolicy? GetPolicy(Type actionType)
        => throw new InvalidOperationException(
            $"No generated policy registry is available, so the policy of '{actionType.Name}' cannot " +
            "be established and the call is refused. The Pragmatic source generator emits one for " +
            "every assembly containing actions; its absence means the generator did not run, or its " +
            "output was discarded — check the build for CS8785, which reports a discarded generator " +
            "output as a warning.");
}
