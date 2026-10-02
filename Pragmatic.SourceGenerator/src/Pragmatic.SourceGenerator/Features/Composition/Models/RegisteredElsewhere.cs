namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     A contract an assembly this one references says it registers, and the lifetime it registers it
///     with — read from that assembly's DI metadata.
/// </summary>
/// <remarks>
///     <para>
///         The dependency validator sees the <c>[Service]</c> classes of its own compilation,
///         so a contract a sibling module registers looked unregistered and was refused — measured on
///         <c>IRegistryReads</c>, which the sibling's generator writes and its generated registration
///         binds. Modules composing into one host is the architecture, and the answer was already
///         written down: it just was not read here.
///     </para>
///     <para>
///         The lifetime travels with the name on purpose. Treating the contract as registered without it
///         would leave a singleton free to capture a per-request service of another module — silence
///         where there had been a wrong report, which is the worse of the two.
///     </para>
/// </remarks>
internal sealed record RegisteredElsewhere
{
    /// <summary>The contract's fully qualified name, as a dependency of a service spells it.</summary>
    public required string Contract { get; init; }

    /// <summary><c>"Singleton"</c>, <c>"Scoped"</c> or <c>"Transient"</c>.</summary>
    public required string Lifetime { get; init; }
}
