// Pragmatic.Composition.HostWiring.Tests - The measurement
// One case per feature. A failure names the feature and the registration line that is missing.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A framework type declared in the host project must be wired exactly as when it is declared in
///     a referenced library.
/// </summary>
/// <remarks>
///     <para>
///         The host wiring is built from the <c>[assembly: PragmaticMetadata]</c> attributes of the
///         compilation's <i>references</i> (<c>MetadataReader.ReadFromReferences</c> iterates
///         <c>compilation.References</c> and nothing else). Those attributes are generated into the
///         same compilation for types declared in the host, so the reader never sees them and the
///         registration is never emitted.
///     </para>
///     <para>
///         Nothing caught this because the Showcase host — the only host in the repository — declares
///         no framework types at all, so the path has never been walked.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class HostDeclaredWiringTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";
    private const string HostEntry = "Host.Entry.g.cs";
    private const string MigrationDbContext = "DbContext.Migration.g.cs";
    private const string AggregatedManifest = "_Metadata.PragmaticManifest.Aggregated.g.cs";
    private const string OpenApi = "_Infra.OpenApi.Generated.g.cs";
    private const string TopologyRegistration = "_Metadata.HostTopology.Registration.g.cs";

    /// <summary>
    ///     Guards the guard: every other case reads the control group, and a control group produced by
    ///     a library that failed to compile would make all of them fail for the wrong reason.
    /// </summary>
    [Fact]
    public void ControlGroup_LibraryDeclaringEveryFeature_CompilesAndEmits()
    {
        fixture.LibraryErrors.Length.Should().Be(0,
            "the probe library must compile, or the control group is empty and the suite measures "
            + $"nothing:{Environment.NewLine}"
            + string.Join(Environment.NewLine, fixture.LibraryErrors.Take(20).Select(e => $"  - {e}")));

        fixture.LibraryEmitErrors.Length.Should().Be(0,
            "the probe library must emit an assembly for the host to reference:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, fixture.LibraryEmitErrors.Take(20).Select(e => $"  - {e}")));
    }

    /// <summary>
    ///     Both hosts must produce the file the wiring lives in; without it the per-feature cases
    ///     report an empty subject and it is not obvious why.
    /// </summary>
    [Fact]
    public void BothShapes_ProduceTheHostServicesFile()
    {
        fixture.Control.ContainsKey(HostServices).Should().BeTrue(
            $"the control host must generate {HostServices}");
        fixture.Subject.ContainsKey(HostServices).Should().BeTrue(
            $"the host that declares its own types must generate {HostServices}");
    }

    /// <summary>
    ///     Proves the comparison can succeed. Every case below asserts that a line found in the
    ///     control is also in the subject; a harness that read the wrong file, or normalised the two
    ///     shapes apart, would fail all of them and look exactly like the defect. These registrations
    ///     come from the referenced modules in both shapes and depend on no declared type, so they
    ///     must match: they are the eleven cases' proof that a match is reachable at all.
    /// </summary>
    /// <remarks>
    ///     Deliberately not <c>services.AddPragmaticCaching();</c>: that line takes a different shape
    ///     once cache categories are discovered, which is what <c>CacheCategoryDiscoveryTests</c>
    ///     measures. A guard that a legitimate fix elsewhere turns red is not a guard.
    /// </remarks>
    [Theory]
    [InlineData("services.AddHybridCache();")]
    [InlineData("services.AddPragmaticIdentity();")]
    [InlineData("services.AddPragmaticMessaging();")]
    [InlineData("services.AddPragmaticJobs();")]
    public void ComparisonHarness_RegistrationsIndependentOfDeclaredTypes_MatchInBothShapes(string registration)
        => WiringAssert.RegistrationReachesHost(fixture, $"the harness ({registration})", HostServices,
            line => line.Equals(registration, StringComparison.Ordinal));

    /// <summary>
    ///     The probe declares actions and <b>no</b> <c>[RequirePermission]</c> anywhere —
    ///     the shape of the first application anybody writes. Both registries must still be emitted:
    ///     the fallbacks they displace do not answer "nothing required", they <b>throw</b>, on purpose,
    ///     so that a silenced generator cannot pass for an assembly that declares nothing. Emitting
    ///     them only when there is something to put inside would turn that hardening into an HTTP 500
    ///     on the first action of every application without authorization.
    /// </summary>
    [Theory]
    [InlineData("IPermissionRequirementRegistry", "HostPermissionRequirementRegistry")]
    [InlineData("IPolicyRegistry", "HostPolicyRegistry")]
    public void Actions_WithoutAnyPermission_TheRegistryIsStillRegistered(string contract, string implementation)
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(l => l.Contains($"AddSingleton<global::Pragmatic.Actions.Pipeline.{contract}>(new {implementation}())", StringComparison.Ordinal),
            $"AddPragmaticActions() uses TryAddSingleton, so without this line the throwing Unavailable{contract[1..]} stays in place");
        lines.Should().Contain(l => l.Contains($"private sealed class {implementation}", StringComparison.Ordinal),
            "the registration must not reference a class that was never rendered");
    }

    /// <summary>Mutation invokers of a <c>[Mutation]</c> declared in the host.</summary>
    /// <remarks>
    ///     ⚠️ The host does not write one <c>AddScoped&lt;IMutationInvoker&lt;…&gt;, X.Invoker&gt;()</c>
    ///     per operation, which would duplicate what each module generates; it calls the module's own
    ///     <c>Add{Prefix}Mutations</c>, the way it calls its boundary extension and its reads
    ///     registration. The invariant — declared in the host wires exactly as declared in a library —
    ///     is the reason the host-local metadata carries the entry point too: without that, this test
    ///     is what notices the two shapes coming apart.
    /// </remarks>
    [Fact]
    public void Actions_MutationDeclaredInHost_IsRegisteredAsInvoker()
        => WiringAssert.RegistrationReachesHost(fixture, "Actions", HostServices,
            line => line.Contains("MutationsRegistrationExtensions.Add", StringComparison.Ordinal));

    /// <summary>
    ///     The host names no generated invoker type of its own — which is what lets one be
    ///     <c>internal</c>.
    /// </summary>
    /// <remarks>
    ///     The control group is a host that <em>references</em> the library declaring the operations:
    ///     the shape where the accessibility mattered, since a type named across an assembly boundary
    ///     has to be public. Asserted on the control, not the subject, because a host declaring its own
    ///     types could name them either way and prove nothing.
    /// </remarks>
    [Fact]
    public void Actions_ReferencedModuleInvokers_AreNotNamedByTheHost()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices).ToList();

        lines.Should().Contain(l => l.Contains("MutationsRegistrationExtensions.Add", StringComparison.Ordinal),
            "the control must still wire the library's mutations somehow, or the absence below means nothing");

        lines.Should().NotContain(l => l.Contains(".Invoker>();", StringComparison.Ordinal),
            "naming a module's generated invoker from the host forces it to be public, which is why an "
            + "invoker taking the module's internal boundary facade needed a service locator");
    }

    /// <summary>
    ///     The lookup-cache registration of a <c>[Lookup]</c> entity.
    /// </summary>
    /// <remarks>
    ///     The generated <c>Add{Prefix}LookupCaches()</c> registers <c>ILookupCache&lt;T, TId&gt;</c>,
    ///     the per-entity <c>ILookupCacheLoader</c> and the hosted service that preloads them — and the
    ///     host called it for nobody, because the host builds its container from the metadata documents
    ///     rather than by calling a module's extension methods. Without it a nullable lookup navigation
    ///     silently returns <c>null</c> and a non-nullable one throws on every read. The Showcase never
    ///     showed it: its startup step calls the method by hand, which is what the documentation told
    ///     the reader to do "even inside a host".
    /// </remarks>
    [Fact]
    public void Persistence_LookupDeclaredInHost_HasItsCacheRegistered()
        => WiringAssert.RegistrationReachesHost(fixture, "Persistence (lookup caches)", HostServices,
            line => line.Contains("LookupCacheRegistrationExtensions", StringComparison.Ordinal));

    /// <summary>Route mapping of an <c>[Endpoint]</c> declared in the host.</summary>
    [Fact]
    public void Endpoints_EndpointDeclaredInHost_IsMapped()
        => WiringAssert.RegistrationReachesHost(fixture, "Endpoints", HostServices,
            line => line.EndsWith(".MapEndpoint(root);", StringComparison.Ordinal));

    /// <summary>
    ///     DI registration of the processor an <c>[Endpoint]</c> names with
    ///     <c>[PreProcessor&lt;T&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     The generated handler resolves it with <c>GetRequiredService</c> and nothing registered it:
    ///     every request to a route carrying a processor answered 500. The control group is the shape
    ///     that failed — a host referencing the library that declares the processor — so an empty
    ///     control here means the registration is missing again.
    /// </remarks>
    [Fact]
    public void Endpoints_ProcessorDeclaredInHost_IsRegisteredForResolution()
        => WiringAssert.RegistrationReachesHost(fixture, "Endpoints (processors)", HostServices,
            line => line.Contains("ProbePreProcessor", StringComparison.Ordinal));

    /// <summary>
    ///     Binding and catalogue of a <c>[Configuration]</c> declared in the host.
    /// </summary>
    /// <remarks>
    ///     Two failures behind one call. The catalogue is what <c>ConfigurationPreflight</c> checks an
    ///     environment against, so a section the host declares itself being absent from it makes the
    ///     check pass on a configuration missing exactly those keys. The binding is what makes the
    ///     section exist at all — without it <c>ValidateOnStart</c> never runs.
    /// </remarks>
    [Fact]
    public void Configuration_SectionDeclaredInHost_IsBoundAndCatalogued()
        => WiringAssert.RegistrationReachesHost(fixture, "Configuration", HostServices,
            line => line.Contains(".AddGeneratedConfiguration(services, configuration);", StringComparison.Ordinal));

    /// <summary>JSON converters for a <c>[FastEnum]</c> declared in the host.</summary>
    [Fact]
    public void FastEnum_EnumDeclaredInHost_ConvertersAreAddedToJsonOptions()
        => WiringAssert.RegistrationReachesHost(fixture, "FastEnum", HostEntry,
            line => line.Contains("PragmaticFastEnumJsonConverters.AddTo(", StringComparison.Ordinal));

    /// <summary>Permission catalog of an <c>[assembly: Permission]</c> declared in the host.</summary>
    [Fact]
    public void Identity_PermissionDeclaredInHost_IsAddedToTheAuthorizationCatalog()
        => WiringAssert.RegistrationReachesHost(fixture, "Identity", HostServices,
            line => line.Contains(".AddGeneratedAuthorizationCatalog(services);", StringComparison.Ordinal));

    /// <summary>Job registry of a <c>[RecurringJob]</c> declared in the host.</summary>
    [Fact]
    public void Jobs_JobDeclaredInHost_IsRegisteredWithTheJobRegistry()
        => WiringAssert.RegistrationReachesHost(fixture, "Jobs", HostServices,
            line => line.Contains(".AddDiscoveredJobs(services);", StringComparison.Ordinal));

    /// <summary>Handler registration of a <c>[MessageHandler]</c> declared in the host.</summary>
    [Fact]
    public void Messaging_HandlerDeclaredInHost_IsRegisteredWithTheBus()
        => WiringAssert.RegistrationReachesHost(fixture, "Messaging", HostServices,
            line => line.Contains(".AddPragmaticMessageHandlers(services);", StringComparison.Ordinal));

    /// <summary>Repository registration of an <c>[Entity]</c> declared in the host.</summary>
    [Fact]
    public void Persistence_EntityDeclaredInHost_HasItsRepositoryRegistered()
        => WiringAssert.RegistrationReachesHost(fixture, "Persistence", HostServices,
            line => line.Contains("Pragmatic.Persistence.Repository.IRepository<", StringComparison.Ordinal));

    /// <summary>Generated <c>JsonSerializerContext</c> of types declared in the host.</summary>
    [Fact]
    public void Serialization_TypesDeclaredInHost_HaveTheirJsonContextRegistered()
        => WiringAssert.RegistrationReachesHost(fixture, "Serialization", HostServices,
            line => line.Contains(".AddGeneratedJsonContext(services);", StringComparison.Ordinal));

    /// <summary>Timezone behaviours of a <c>[ToClientTimezone]</c> property declared in the host.</summary>
    [Fact]
    public void Temporal_TimezoneBehaviourDeclaredInHost_IsRegistered()
        => WiringAssert.RegistrationReachesHost(fixture, "Temporal", HostServices,
            line => line.Contains("TemporalBehaviorExtensions.Add", StringComparison.Ordinal));

    /// <summary>
    ///     The child entity of a <c>[HasComments]</c> declared in the host must reach the migration
    ///     DbContext — the only host-side consumer of <c>MetadataCategory.TraitEntities</c> (19).
    /// </summary>
    /// <remarks>
    ///     When the whole file is absent the host has no migration DbContext at all, which is the
    ///     Persistence side of the same defect; the failure message says so.
    /// </remarks>
    [Fact]
    public void Traits_CommentTraitDeclaredInHost_ReachesTheMigrationDbContext()
        => WiringAssert.RegistrationReachesHost(fixture, "Traits", MigrationDbContext,
            line => line.Contains("ProbeEntityComment", StringComparison.Ordinal));

    /// <summary>Registration of a <c>[Validator]</c> declared in the host.</summary>
    [Fact]
    public void Validation_ValidatorDeclaredInHost_IsRegistered()
        => WiringAssert.RegistrationReachesHost(fixture, "Validation", HostServices,
            line => line.Contains(".AddGeneratedValidators(services);", StringComparison.Ordinal));

    // =========================================================================
    // The two channels that survived the first repair, for reasons of their own.
    // =========================================================================

    /// <summary>
    ///     The aggregated manifest merges the per-module manifests the host can see. The host's own
    ///     manifest is written by this same run and reaches the aggregation model — but only after
    ///     <c>GenerateAggregatedManifest</c> has already run, so it merged nothing and the file was not
    ///     emitted at all.
    /// </summary>
    [Fact]
    public void Manifest_ModuleDeclaredInHost_IsMergedIntoTheAggregatedManifest()
        => WiringAssert.RegistrationReachesHost(fixture, "Manifest", AggregatedManifest,
            line => line.Equals("internal static class PragmaticManifest", StringComparison.Ordinal)
                    || line.StartsWith("internal const int ModuleCount =", StringComparison.Ordinal));

    /// <summary>
    ///     The compile-time OpenAPI document is built from the same manifest list, so it disappeared
    ///     with it.
    /// </summary>
    [Fact]
    public void OpenApi_EndpointsDeclaredInHost_ProduceTheCompileTimeDocument()
        => WiringAssert.RegistrationReachesHost(fixture, "OpenApi", OpenApi,
            line => line.Equals("internal static class PragmaticOpenApi", StringComparison.Ordinal));

    /// <summary>
    ///     A <c>[Boundary]</c> declared in the host: its invokers are registered, its interface is not.
    /// </summary>
    /// <remarks>
    ///     A separate channel from the eleven repaired before it — <c>[PragmaticModuleMetadata]</c>, read
    ///     by <c>ReadDomainModulesFromReferences</c>, not <c>[PragmaticMetadata]</c>. Same wall: the
    ///     attribute is emitted into the compilation being analysed.
    /// </remarks>
    [Fact]
    public void Composition_BoundaryDeclaredInHost_HasItsInterfaceRegistered()
        => WiringAssert.RegistrationReachesHost(fixture, "Composition (boundary)", HostServices,
            line => line.EndsWith(".AddProbeBoundary(services);", StringComparison.Ordinal));

    /// <summary>
    ///     The topology metadata is gated on there being includes or domain modules to describe, so the
    ///     same blind channel also silenced it.
    /// </summary>
    [Fact]
    public void Composition_BoundaryDeclaredInHost_AppearsInTheTopologyMetadata()
        => WiringAssert.RegistrationReachesHost(fixture, "Composition (topology attribute)", HostServices,
            line => line.StartsWith("[assembly: PragmaticMetadata(MetadataCategory.HostTopology",
                StringComparison.Ordinal));

    /// <summary>The zero-reflection provider that makes the topology readable at run time.</summary>
    [Fact]
    public void Composition_BoundaryDeclaredInHost_GetsTheTopologyMetadataProvider()
        => WiringAssert.RegistrationReachesHost(fixture, "Composition (topology provider)", TopologyRegistration,
            line => line.StartsWith("internal sealed class PragmaticHostTopologyMetadataProvider",
                StringComparison.Ordinal));
}
