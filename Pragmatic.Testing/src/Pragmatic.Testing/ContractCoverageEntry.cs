namespace Pragmatic.Testing;

/// <summary>
///     One published operation, and what the contract generator emitted for it.
/// </summary>
/// <param name="Boundary">The boundary the operation belongs to.</param>
/// <param name="Operation">
///     The generated tests' own name for it — the endpoint class's name without an <c>Endpoint</c>
///     suffix.
/// </param>
/// <param name="HttpMethod">The verb, as the endpoint declares it.</param>
/// <param name="Route">The full route, group prefixes included.</param>
/// <param name="Contracts">
///     What was emitted, in the order the generator decided it: <c>auth</c>, <c>create</c>,
///     <c>validation</c>, <c>isolation</c>, <c>not-found</c>, <c>transition</c>. Empty when nothing was.
/// </param>
/// <param name="NotCovered">
///     Why nothing more was emitted, one reason per contract the generator considered and declined.
///     Empty when the operation got everything its shape allows.
/// </param>
/// <remarks>
///     ⚠️ <b>This exists because a generated suite says what it did and never what it skipped.</b> Reading
///     the emitted classes tells you eight operations are covered; it cannot tell you the application
///     publishes eleven. Without this, the only way to find the operations with no contract is to
///     compare the generated names against the route table by hand.
/// </remarks>
public sealed record ContractCoverageEntry(
    string Boundary,
    string Operation,
    string HttpMethod,
    string Route,
    IReadOnlyList<string> Contracts,
    IReadOnlyList<string> NotCovered);
