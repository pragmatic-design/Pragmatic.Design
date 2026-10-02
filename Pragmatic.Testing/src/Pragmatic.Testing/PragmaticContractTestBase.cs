namespace Pragmatic.Testing;

/// <summary>
///     Base for generated contract test classes (#7): exposes the <see cref="HttpClient"/> aimed at the
///     application under test. The client comes from <see cref="PragmaticContractHost"/>, which the consumer's
///     collection fixture sets once when it boots the app — so the generated classes (all in the
///     <c>PragmaticContractTests</c> xUnit collection) need no per-class wiring. This is the escape hatch:
///     one fixture per app instead of one partial per generated class.
/// </summary>
public abstract class PragmaticContractTestBase
{
    /// <summary>
    ///     The boundary whose contracts this class holds. The generator overrides it with the boundary's
    ///     own name, which it has: the class is <c>IntakeAuthContractTests</c>.
    /// </summary>
    /// <remarks>
    ///     Empty here so that a hand-written class deriving from this base keeps the single-service
    ///     behaviour — <see cref="PragmaticContractHost.ClientFor" /> answers with
    ///     <see cref="PragmaticContractHost.Client" /> while no boundary has a client of its own.
    /// </remarks>
    protected virtual string Boundary => "";

    /// <summary>The HTTP client targeting the running application under test (set by the collection fixture).</summary>
    /// <remarks>
    ///     ⚠️ Per boundary, not per application: an application whose services are separate processes
    ///     registers one client each (<see cref="PragmaticContractHost.UseClientFor" />) and this sends
    ///     each half of the generated suite to the host that owns it, instead of a prefix table in the
    ///     consumer's fixture.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Instance member by design — the generated test classes reference it as inherited 'Client'.")]
    protected HttpClient Client => PragmaticContractHost.ClientFor(Boundary);
}
