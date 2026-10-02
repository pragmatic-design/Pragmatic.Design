using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The manifest a host enriches its runtime document from is its own.
/// </summary>
/// <remarks>
///     <para>
///         <c>ManifestRegistry</c> is one list for the whole process and the generated host adds its
///         aggregate to it from a <c>[ModuleInitializer]</c>. ⚠️ It <b>accumulates</b> rather than
///         overwrites, and the difference matters: two hosts in one process do not lose a manifest,
///         they share both, and an endpoint lookup built from the registry is built from the union.
///     </para>
///     <para>
///         The enrichment itself is driven by the document's own operations, so another host's entries
///         are never reached by a route this host does not serve. What is <b>not</b> route-scoped is
///         <c>requiresAuthentication</c>: it is <c>Any()</c> over the whole lookup, and it decides
///         whether the published document declares security schemes at all. So an anonymous host
///         sharing a process with an authenticated one publishes schemes for operations it does not
///         have — the same symptom the compile-time document had, one registry along.
///     </para>
/// </remarks>
public class TheManifestBelongsToTheHostThatWasAskedTests
{
    /// <summary>A host whose every operation is anonymous.</summary>
    private const string AnonymousHost = """
        {
          "assembly": "Kiosk",
          "endpoints": [
            { "httpMethod": "GET", "fullRoute": "/api/prices", "authorization": { "allowAnonymous": true } }
          ]
        }
        """;

    /// <summary>Another host, in the same process, whose operations are not.</summary>
    private const string ProtectedHost = """
        {
          "assembly": "BackOffice",
          "endpoints": [
            { "httpMethod": "POST", "fullRoute": "/api/prices", "authorization": { "allowAnonymous": false, "requiredPermissions": ["prices.write"] } }
          ]
        }
        """;

    /// <summary>The host's own manifest is what it reads, when it registered one.</summary>
    [Fact]
    public void AHostThatRegisteredItsOwnManifest_ReadsOnlyThatOne()
    {
        var services = new ServiceCollection()
            .AddSingleton(new HostManifest(AnonymousHost))
            .BuildServiceProvider();

        var manifests = ManifestReader.ReadFor(services);

        manifests.Should().ContainSingle().Which.Assembly.Should().Be("Kiosk");
    }

    /// <summary>
    ///     ⚠️ And the consequence that matters: the other host's protected operation does not
    ///     decide whether this one publishes security schemes.
    /// </summary>
    /// <remarks>
    ///     <c>requiresAuthentication</c> is <c>Any(e =&gt; !AllowAnonymous)</c> over the lookup the
    ///     transformer builds. Over both manifests it is true for a host that has no protected operation
    ///     at all; over its own it is false, which is the answer about the host that was asked.
    /// </remarks>
    [Fact]
    public void AnAnonymousHost_DoesNotInheritTheOtherHostsAuthentication()
    {
        var shared = ManifestReader.Parse([AnonymousHost, ProtectedHost]);
        var itsOwn = ManifestReader.Parse([AnonymousHost]);

        RequiresAuthentication(shared).Should().BeTrue(
            "read across the process, the other host's protected operation is in the lookup");
        RequiresAuthentication(itsOwn).Should().BeFalse(
            "and read from this host's own manifest, nothing here needs authentication");
    }

    /// <summary>
    ///     The control: a host that registered nothing reads the process registry.
    /// </summary>
    /// <remarks>
    ///     Without the fallback, every host generated without its own manifest would have its runtime
    ///     enrichment emptied — silently, which is the failure mode this class guards against.
    /// </remarks>
    [Fact]
    public void AHostThatRegisteredNothing_ReadsWhateverTheProcessHas()
    {
        var empty = new ServiceCollection().BuildServiceProvider();

        ManifestReader.ReadFor(empty).Should().BeEquivalentTo(ManifestReader.ReadAll());
    }

    /// <summary>And no container at all is the same fallback, not an exception.</summary>
    [Fact]
    public void NoServicesAtAll_IsTheSameFallback()
        => ManifestReader.ReadFor(null).Should().BeEquivalentTo(ManifestReader.ReadAll());

    /// <summary>
    ///     The second control: reading its own manifest does not cost the host its enrichment — the
    ///     lookup still finds the route it serves.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this, "the host reads only its own" would be satisfied by reading nothing: an empty
    ///     lookup makes the transformer return early and the document comes out unenriched, which is a
    ///     silent regression of exactly the shape this file exists for.
    /// </remarks>
    [Fact]
    public void ItsOwnManifest_StillEnrichesTheRoutesItServes()
    {
        var lookup = ManifestOpenApiTransformer.BuildEndpointLookup(
            ManifestReader.Parse([AnonymousHost]), routePrefix: "");

        // The key is the normalised route, which has no leading slash — the document's paths are
        // normalised the same way before the lookup is asked.
        lookup.Should().ContainKey("GET:api/prices");
    }

    private static bool RequiresAuthentication(IReadOnlyList<ManifestDocument> manifests)
        => ManifestOpenApiTransformer.BuildEndpointLookup(manifests, routePrefix: "")
            .Values.Any(e => e.Authorization is { AllowAnonymous: false });
}
