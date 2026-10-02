using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Pragmatic.Testing;

/// <summary>
///     Holds the <see cref="HttpClient"/> the generated contract tests run against (#7). The consumer's
///     collection fixture (which boots the real app over its database) assigns <see cref="Client"/> in its
///     async initialization; every generated test class reads it through <see cref="PragmaticContractTestBase"/>.
///     All generated classes share the single <c>PragmaticContractTests</c> collection, so xUnit runs them
///     serially and this static is set once and never raced.
/// </summary>
public static class PragmaticContractHost
{
    /// <summary>The xUnit collection every generated contract-test class belongs to.</summary>
    public const string Collection = "PragmaticContractTests";

    private static readonly Lock PerBoundaryGate = new();

    private static readonly Dictionary<string, HttpClient> PerBoundary =
        new(StringComparer.OrdinalIgnoreCase);

    private static HttpClient? _client;

    /// <summary>The client targeting the running app. Set by the collection fixture before any test runs.</summary>
    /// <remarks>
    ///     The whole answer for an application that is one service. One made of several registers a client
    ///     per boundary with <see cref="UseClientFor" /> instead; see <see cref="ClientFor" /> for which
    ///     of the two a generated test gets.
    /// </remarks>
    public static HttpClient Client
    {
        get => _client ?? throw new System.InvalidOperationException(
            $"{nameof(PragmaticContractHost)}.{nameof(Client)} was not set. The consumer must define a collection " +
            $"fixture for the '{Collection}' collection that boots the app and assigns it.");
        set => _client = value;
    }

    /// <summary>
    ///     The client for one boundary's contract tests, for an application whose services are separate
    ///     processes.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The boundary is what the generated test class already knows about itself
    ///     (<c>IntakeAuthContractTests</c>), and a request's path is not: an application with two services
    ///     that routes by route prefix behind one client sends a route added to the second service under a
    ///     prefix the table does not name to the first one, which answers 404 — read as a contract failure
    ///     about the wrong service.
    /// </remarks>
    public static void UseClientFor(string boundary, HttpClient client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        ArgumentNullException.ThrowIfNull(client);

        lock (PerBoundaryGate)
            PerBoundary[boundary] = client;
    }

    /// <summary>
    ///     The client a boundary's generated tests send through: the one registered for it, or
    ///     <see cref="Client" /> when the application registered none.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Once any boundary has a client of its own, one that has none is an error rather than a
    ///     fallback.</b> Falling back to <see cref="Client" /> there would send a second service's
    ///     contracts to the first one and report the 404 as a contract failure — which is the thing this
    ///     seam exists to stop. A boundary added to a multi-service application has to be registered, and
    ///     this says so by name.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     No client is registered for <paramref name="boundary" />, and none can be inferred.
    /// </exception>
    public static HttpClient ClientFor(string boundary)
    {
        lock (PerBoundaryGate)
        {
            if (PerBoundary.TryGetValue(boundary ?? "", out var registered))
                return registered;

            if (PerBoundary.Count > 0)
                throw new InvalidOperationException(
                    $"No contract client is registered for boundary '{boundary}'. This application "
                    + $"registered one for {string.Join(", ", PerBoundary.Keys.Order(StringComparer.Ordinal))}, "
                    + $"so {nameof(Client)} is not used as a fallback: sending this boundary's contracts to "
                    + "another service's host answers 404 and reads as a contract failure about the wrong "
                    + $"service. Call {nameof(UseClientFor)}(\"{boundary}\", client) in the fixture of the "
                    + $"'{Collection}' collection.");
        }

        return Client;
    }

    /// <summary>
    ///     A body the application considers valid for a named create, or <c>null</c> to use the
    ///     synthesised one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The generator fills a request from the operation's <b>shape</b>. A create whose validity
    ///     depends on more than the shape — a value that must already exist, a pair that must agree, a
    ///     name that must be unique — is refused before it reaches the handler, and the test then fails
    ///     on the generator's assumption rather than on the application. Four creates in the first real
    ///     consumer were exactly that.
    ///     <para>
    ///         The operation name is the generated test's own: <c>CreateWorkspaceMutation</c>,
    ///         <c>GrantRolePermissionMutation</c>. Return <c>null</c> for the ones the shape can carry,
    ///         which is most of them.
    ///     </para>
    /// </remarks>
    public static Func<string, object?>? BodyFor { get; set; }

    /// <summary>
    ///     The application's last word on a contract request, after the generated identity is written.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A permission is not always the whole authority. A tenancy operation needs a caller with
    ///         a tenant; a host that maps roles to permissions — <c>UseAuthorization(authz => …)</c>
    ///         installs a resolver that derives permissions from roles and stops honouring raw
    ///         permission claims — needs a role. The generator emits permissions and cannot know either,
    ///         because both are host wiring rather than module metadata.
    ///     </para>
    ///     <para>
    ///         So the application gets the request last and may add or replace anything on it. The
    ///         operation name is the generated test's own.
    ///     </para>
    /// </remarks>
    public static Action<ContractRequest>? PrepareRequest { get; set; }

    /// <summary>
    ///     Hands the request to the application, if it asked for it. Called by every generated contract
    ///     test after it has written the identity it invented, with the boundary the test class belongs
    ///     to — which is what an application of several services needs to pick a signing key or a role
    ///     table, and the one thing the request does not carry.
    /// </summary>
    public static HttpRequestMessage Prepare(HttpRequestMessage request, string operation, string boundary)
    {
        PrepareRequest?.Invoke(new ContractRequest(boundary, operation, request));
        return request;
    }

    /// <summary>
    ///     The body for a named create: the application's, when it supplies one, and otherwise the
    ///     synthesised one the generator built from the shape.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="synthesised" /> is <see langword="null" /> when the generator could not fill
    ///         a body at all — a required member it cannot invent: a foreign key, or a nested collection of
    ///         another mutation, which is how an aggregate that carries its children is written. The test
    ///         is emitted all the same and this throws, naming the create, because the alternative was
    ///         emitting neither the create nor the tenant-isolation test and saying nothing: a
    ///         suite that emits one test where it could emit three looks exactly like one that passes.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Neither the generator nor the application can produce a body for this create.
    /// </exception>
    public static object Body(string operation, object? synthesised)
        => BodyFor?.Invoke(operation)
           ?? synthesised
           ?? throw new InvalidOperationException(
               $"The contract test for '{operation}' needs a body from the application: the generator "
               + "could not fill one from the operation's shape, because a required member is a value it "
               + "cannot invent — a foreign key, or a nested collection of another mutation. Set "
               + $"{nameof(PragmaticContractHost)}.{nameof(BodyFor)} in the fixture of the '{Collection}' "
               + $"collection and return a body it considers valid for \"{operation}\"; return null for "
               + "the creates the shape can carry, which is most of them.");

    /// <summary>
    ///     How the application brings an entity that is not created over HTTP into its initial state, for
    ///     the state-transition contracts: given the entity's name and the client of its boundary, create
    ///     one and return its id — or <c>null</c> for an entity it does not arrange.
    /// </summary>
    /// <remarks>
    ///     An entity that only ever comes into being as a consequence — an invoice a confirmed booking
    ///     raises, a verification a case asks for — has no create route for the generator to call. The
    ///     generated contract is a real test, and this is the one thing it cannot write itself.
    /// </remarks>
    public static Func<string, HttpClient, Task<string?>>? ArrangeFor { get; set; }

    /// <summary>
    ///     The id of an <paramref name="entity" /> in its initial state, from <see cref="ArrangeFor" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">The application arranges no such entity.</exception>
    public static async Task<string> ArrangeAsync(string entity, HttpClient client)
        => (ArrangeFor is null ? null : await ArrangeFor(entity, client).ConfigureAwait(false))
           ?? throw new InvalidOperationException(
               $"The state-transition contracts of '{entity}' need one in its initial state, and it is not "
               + "created over HTTP, so the generator cannot make one. Set "
               + $"{nameof(PragmaticContractHost)}.{nameof(ArrangeFor)} in the fixture of the '{Collection}' "
               + $"collection: bring a \"{entity}\" into its initial state and return its id.");

    /// <summary>Forgets what a fixture set. For a test that has to start from nothing.</summary>
    public static void Reset()
    {
        _client = null;
        BodyFor = null;
        PrepareRequest = null;
        ArrangeFor = null;

        lock (PerBoundaryGate)
            PerBoundary.Clear();
    }
}
