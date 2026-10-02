namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     The aggregated API manifest of <b>this host</b>, registered in this host's container by the
///     generated composition and read by the runtime document's transformer from the request's services.
/// </summary>
/// <param name="Json">The aggregated manifest the generator wrote for this host.</param>
/// <remarks>
///     <para>
///         ⚠️ <b>Why a service and not the process registry.</b> <see cref="Manifest.ManifestRegistry" />
///         is one list for the whole process, and the generated host adds its aggregate to it from a
///         <c>[ModuleInitializer]</c>. It accumulates rather than overwrites — so two hosts in one
///         process do not lose a manifest, they <b>share</b> both — and
///         <c>ManifestOpenApiTransformer</c> builds its endpoint lookup from everything it finds.
///     </para>
///     <para>
///         What that costs is narrower than losing a document and harder to see. The enrichment itself
///         is driven by the document's own operations, so another host's entries are never reached by a
///         route this host does not serve. But <c>requiresAuthentication</c> — which decides whether the
///         published document declares security schemes at all — is computed over <b>every</b> endpoint
///         in the lookup, so an anonymous host sharing a process with an authenticated one publishes
///         schemes for operations it does not have; and two hosts serving the same verb and route take
///         whichever entry the lookup kept.
///     </para>
///     <para>
///         A manifest is a fact about one host, so it belongs to that host's container. The registry
///         stays as the fallback for a host that registers nothing — every single-host deployment
///         behaves exactly as it did — and the transformer prefers the per-host answer. The same shape
///         as <see cref="HostOpenApiDocument" />, one registry along.
///     </para>
/// </remarks>
public sealed record HostManifest(string Json);
