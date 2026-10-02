namespace Pragmatic.Composition.Metadata;

/// <summary>
///     Categories for Pragmatic metadata discovery.
///     Each category corresponds to a specific source generator's output.
/// </summary>
public enum MetadataCategory
{
    /// <summary>Dependency injection registrations ([Service], [Decorator], [Inject])</summary>
    DI = 0,

    /// <summary>Object mapping configurations ([MapFrom], [MapTo])</summary>
    Mapping = 1,

    /// <summary>Domain actions ([DomainAction])</summary>
    Actions = 2,

    /// <summary>Startup/middleware modules (IStartupStep)</summary>
    Startup = 3,

    /// <summary>Validation rules ([Validate])</summary>
    Validation = 4,

    /// <summary>API endpoints ([Endpoint])</summary>
    Endpoints = 5,

    /// <summary>Health checks ([HealthCheck])</summary>
    HealthChecks = 6,

    /// <summary>Identifier configurations ([GeneratedValue])</summary>
    Identifiers = 7,

    /// <summary>Module definitions and dependencies ([Module])</summary>
    Module = 8,

    /// <summary>Translation keys and embedded strings (TranslationKeys)</summary>
    Translations = 9,

    /// <summary>Persistence entities, repositories, and DbContext configurations ([Entity], [Database])</summary>
    Persistence = 10,

    /// <summary>Domain event handler registrations ([EventHandler])</summary>
    EventHandlers = 11,

    /// <summary>Host deployment topology: module to database to DbContext wiring</summary>
    HostTopology = 12,

    /// <summary>Cache configuration and invalidation ([Cacheable], [InvalidatesCache])</summary>
    Caching = 13,

    /// <summary>Configuration options metadata ([Configuration] — section paths, properties, validation rules)</summary>
    Configuration = 14,

    /// <summary>Message handler registrations ([MessageHandler])</summary>
    MessageHandlers = 15,

    /// <summary>Background job registrations ([Job], [RecurringJob])</summary>
    Jobs = 16,

    /// <summary>Saga registrations ([Saga&lt;TState&gt;])</summary>
    Sagas = 17,

    // ─────────────────────────────────────────────────────────────────────────
    // Every number the generator emits is named here, including 18–22. Nothing
    // enforces the correspondence: a category emitted as a numeric cast —
    // (MetadataCategory)19 — and missing from this enum is invisible to the next
    // feature choosing a number, and picking a used one would make two payloads
    // indistinguishable to the host reader.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>API manifest emitted for the client generator.</summary>
    Manifest = 18,

    /// <summary>Trait-generated entities, for host DbContext and schema generation.</summary>
    TraitEntities = 19,

    /// <summary>Generated JsonSerializerContext registrations.</summary>
    JsonContexts = 20,

    /// <summary>Timezone conversion behaviors for DTO properties.</summary>
    TemporalBehaviors = 21,

    /// <summary>Generated authorization/permission catalog.</summary>
    Authorization = 22,

    /// <summary>
    ///     Personal-data classification: which properties are personal, of what category, how they are
    ///     erased, and what is retained and why.
    /// </summary>
    /// <remarks>
    ///     The payload behind this category is what the Article 30 processing register is derived from.
    ///     Deriving it from the code is the point: a register maintained by hand describes the system as
    ///     someone remembered it, which is rarely the system that is running.
    /// </remarks>
    PersonalData = 23,

    /// <summary>Generated [FastEnum] JSON converter registrations.</summary>
    FastEnumConverters = 24,

    /// <summary>
    ///     Generated <c>IRedactionMap</c> registrations — the members marked <c>[NotLogged]</c> or
    ///     <c>[PersonalData]</c>.
    /// </summary>
    /// <remarks>
    ///     Without this entry the attributes are inert: the generator emits the map and an
    ///     <c>AddGeneratedRedactionMap</c> to register it, but the host learns what to call from these
    ///     metadata entries, so nothing would call it and a field declared "do not log" would be logged.
    /// </remarks>
    Redaction = 25,

    /// <summary>
    ///     Generated <c>RollUpRule</c> registrations — the stored aggregates a parent keeps over its
    ///     children, declared with <c>[RollUp&lt;TChild&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     Without this entry the attribute is inert in exactly the way <see cref="Redaction" />
    ///     records: the generator emits the rules and a method to register them, the interceptor asks
    ///     the container for them, and nothing would put them there. A declared aggregate would stay
    ///     at its default — no error, no log, a number that is simply never maintained.
    /// </remarks>
    RollUpRules = 26,

    /// <summary>
    ///     The read contracts a <c>[Published]</c> query produces — <c>I{Module}Reads</c> and its
    ///     implementation — and the registration that binds them.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this entry the generator would emit the interface, the implementation and
    ///     <c>Add{Module}Reads()</c> with no metadata beside them, so the host would have nothing to
    ///     read and would call nothing: without a hand-written line the consuming action fails at
    ///     resolution, at the <b>first request</b> rather than at startup. It is the same shape as the
    ///     lookup caches, one floor down.
    /// </remarks>
    ReadContracts = 27,

    /// <summary>
    ///     The resilience policies an assembly declares — <c>[ResiliencePolicy]</c>,
    ///     <c>[Retry]</c>, <c>[Timeout]</c>, <c>[CircuitBreaker]</c>.
    /// </summary>
    /// <remarks>
    ///     Without a declaration to read, the host would wire the capability from the assembly being on
    ///     the compilation — which it is whether the application asked for it or somebody else's
    ///     dependency brought it transitively. A host that uses resilience in zero files would still
    ///     register it, and removing the package reference would change nothing.
    /// </remarks>
    Resilience = 28,

    /// <summary>
    ///     The feature flags an assembly declares — the <see cref="Pragmatic.FeatureFlags.IFeatureFlag" />
    ///     implementations it names.
    /// </summary>
    /// <remarks>
    ///     The sixth capability moved off presence, and the one that differs from the five before it:
    ///     a flag is declared by <b>implementing an interface</b>, not by writing an attribute, so the
    ///     feature that reads it cannot use <c>ForAttributeWithMetadataName</c>. It was left out of the
    ///     first sweep because nobody had checked whether it had a declaration site at all.
    /// </remarks>
    FeatureFlags = 29,

    /// <summary>
    ///     That an assembly takes a value from the clock — a <c>[FromClock]</c> property on one of its
    ///     operations, which the generated invoker fills from the registered <c>IClock</c>.
    /// </summary>
    /// <remarks>
    ///     The one category that exists so a host can be <b>told</b> rather than wired: the declaration
    ///     and the implementation are in different packages. <c>[FromClock]</c> and <c>IClock</c> live in
    ///     Abstractions, so any module can declare one; <c>SystemClock</c> and
    ///     <c>AddPragmaticTemporal()</c> live in Pragmatic.Temporal, which is not in a host's default
    ///     package set. A host that cannot name the clock cannot register it, so what it can do is refuse
    ///     to leave the author to find out on the first request — measured as a 500 on the one operation
    ///     that needed a date.
    /// </remarks>
    ClockBindings = 30,

    /// <summary>
    ///     That an assembly puts one of the internationalization value types on the wire — a
    ///     <c>Money</c>, a <c>CurrencyCode</c>, a <c>CultureCode</c> — in an endpoint's result, in a
    ///     request member or in a member of either.
    /// </summary>
    /// <remarks>
    ///     Their JSON converters are installed by <c>AddPragmaticInternationalization</c>, which the
    ///     generated host calls only where i18n was <b>declared</b>: a translation file. An application
    ///     with an amount on the wire, no translation file and no <c>UseI18N</c> call therefore got no
    ///     converters, and every request carrying a <c>Money</c> was a <b>400 before any rule ran</b> —
    ///     while the application started perfectly, because with no i18n at all it never reaches the
    ///     configuration check that would have refused.
    ///     <para>
    ///         So the shape on the wire is a declaration of its own, and this category is it: the host
    ///         installs the converters alone, which is what such an application asked for and all of it.
    ///     </para>
    /// </remarks>
    I18nWireTypes = 31,

    /// <summary>
    ///     That an assembly embeds document or mail templates (<c>.pdxdoc</c>, <c>.pdxemail</c>) and
    ///     declares it with <c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     The host registers the assembly as a template source. Without it every host that included a
    ///     module with templates called <c>AddPdxTemplates</c> by hand, because a module's startup step is
    ///     not discovered across assemblies — the module declared nothing, and the host guessed.
    /// </remarks>
    PdxTemplates = 32,

    /// <summary>
    ///     That an assembly declares a response the server may keep for any caller —
    ///     <c>[ResponseCache]</c> with its default location.
    /// </summary>
    /// <remarks>
    ///     The route carries <c>CacheOutput(…)</c>, which is metadata: the host adds
    ///     <c>AddOutputCache</c> and the middleware because somebody declared one, not because the
    ///     package is on the compilation. Before this, nothing added them and the attribute kept nothing.
    /// </remarks>
    OutputCache = 33
}
