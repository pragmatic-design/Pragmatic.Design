namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Stands in for <see cref="IPermissionRequirementRegistry" /> when the source generator produced
///     none, and refuses every question instead of answering "no requirement".
/// </summary>
/// <remarks>
///     <para>
///         The generator emits a registry for every assembly that has actions, including one where no
///         action declares a permission — that case yields an empty registry, not a missing one. So
///         reaching this type means the generated registry is absent, and the only honest answer to
///         "what does this action require?" is that nobody knows.
///     </para>
///     <para>
///         It is not an empty registry answering <see langword="null" />, which the filter reads
///         as "nothing required" and allows. That would make a silent generator indistinguishable
///         from an assembly with no declarations — and a generator can go silent while the build stays green,
///         because a duplicate hint name makes Roslyn discard a generator's entire output behind a
///         warning. Every <c>[RequirePermission]</c> in the assembly would then pass.
///     </para>
/// </remarks>
internal sealed class UnavailablePermissionRequirementRegistry : IPermissionRequirementRegistry
{
    public static readonly UnavailablePermissionRequirementRegistry Instance = new();

    public PermissionRequirementEntry? GetRequirement(Type actionType)
        => throw new InvalidOperationException(
            $"No generated permission registry is available, so the requirements of '{actionType.Name}' " +
            "cannot be established and the call is refused. The Pragmatic source generator emits one " +
            "for every assembly containing actions; its absence means the generator did not run, or " +
            "its output was discarded — check the build for CS8785, which reports a discarded " +
            "generator output as a warning.");
}
