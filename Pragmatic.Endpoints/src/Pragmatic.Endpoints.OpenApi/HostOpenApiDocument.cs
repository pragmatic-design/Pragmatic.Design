namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     The compile-time OpenAPI document of <b>this host</b>, registered in this host's container by the
///     generated composition and read by <c>MapPragmaticOpenApi</c> from the request's services.
/// </summary>
/// <param name="Json">The OpenAPI 3.1 JSON document the generator wrote for this host.</param>
/// <param name="RequiresAuthentication">
///     Whether at least one of this host's operations is not <c>[AllowAnonymous]</c>. It travels with the
///     document because it is a fact about the same host: which operations need authentication is settled
///     at compile time, while what the requirement looks like on the wire comes from whoever set the
///     authentication up, and the mount composes the two.
/// </param>
/// <remarks>
///     <para>
///         ⚠️ <b>Why a service and not a static.</b> <see cref="PragmaticOpenApiRegistry" /> is one
///         field for the whole process, written by a <c>[ModuleInitializer]</c> the generator emits per
///         host. Module initializers run at assembly load, in load order, and last writer wins, so
///         <b>two hosts in one process reading the static make the one loaded second answer for
///         both</b>: on a two-service application, <c>GET /openapi/v1.json</c> against the first
///         service returns the second service's document. <c>RequiresAuthentication</c> is overwritten
///         with it, so the published security requirement belongs to the other host too.
///     </para>
///     <para>
///         A document is a fact about one host, so it belongs to that host's container. The static
///         registry is the fallback for a host that registers nothing — a single-host deployment needs
///         nothing more — and the mount prefers the per-host answer.
///     </para>
/// </remarks>
public sealed record HostOpenApiDocument(string Json, bool RequiresAuthentication = false);
