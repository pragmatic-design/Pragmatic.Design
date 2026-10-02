namespace Pragmatic.SourceGenerator.Features.Composition;

/// <summary>
///     The metadata category ordinals, as the strings <c>[assembly: PragmaticMetadata]</c> carries.
///     Mirrors <c>Pragmatic.Composition.Metadata.MetadataCategory</c>.
/// </summary>
/// <remarks>
///     <para>
///         This is a mirror and not a projection, deliberately. The generator targets netstandard2.0
///         and cannot reference the runtime assembly; linking the enum in as source instead puts a
///         second public <c>MetadataCategory</c> in this assembly, and anything referencing both —
///         the test projects today — stops compiling with CS0433. Tried, measured, reverted.
///     </para>
///     <para>
///         What holds the two lists together is therefore a test, <c>MetadataCategoryIdsTests</c>,
///         which can see both assemblies precisely because it references both. It checks all three
///         directions: every category has an entry, every entry names a real category, and each
///         entry carries that category's ordinal. Keep this list <b>complete</b> — a member here for
///         every member of the enum, even one nothing reads yet. The gap that cost us was
///         <c>PersonalData = 23</c>, emitted through the named enum member rather than a cast, so it
///         never appeared here and 23 read as free while it was taken.
///     </para>
/// </remarks>
internal static class MetadataCategoryIds
{
    public const string DI = "0";
    public const string Mapping = "1";
    public const string Actions = "2";
    public const string Startup = "3";
    public const string Validation = "4";
    public const string Endpoints = "5";
    public const string HealthChecks = "6";
    public const string Identifiers = "7";
    public const string Module = "8";
    public const string Translations = "9";
    public const string Persistence = "10";
    public const string EventHandlers = "11";
    public const string HostTopology = "12";
    public const string Caching = "13";
    public const string Configuration = "14";
    public const string MessageHandlers = "15";
    public const string Jobs = "16";
    public const string Sagas = "17";
    public const string Manifest = "18";
    public const string TraitEntities = "19";
    public const string JsonContexts = "20";
    public const string TemporalBehaviors = "21";
    public const string Authorization = "22";
    public const string PersonalData = "23";
    public const string FastEnumConverters = "24";
    public const string Redaction = "25";
    public const string RollUpRules = "26";

    /// <summary>ReadContracts = 27 — the <c>I{Module}Reads</c> a <c>[Published]</c> query produces.</summary>
    public const string ReadContracts = "27";

    /// <summary>
    ///     Resilience = 28 — the policies an assembly declares, so a host wires the capability
    ///     because somebody asked for it rather than because the package is on the compilation.
    /// </summary>
    public const string Resilience = "28";

    /// <summary>
    ///     FeatureFlags = 29 — the <c>IFeatureFlag</c> implementations an assembly names, so a host
    ///     wires the capability because somebody declared a flag rather than because the package is
    ///     on the compilation.
    /// </summary>
    public const string FeatureFlags = "29";

    /// <summary>
    ///     ClockBindings = 30 — somebody's operation takes a value from the clock, so a host that
    ///     cannot name one says so at build time instead of answering 500 to that route.
    /// </summary>
    public const string ClockBindings = "30";

    /// <summary>
    ///     I18nWireTypes = 31 — somebody puts a <c>Money</c> (or another internationalization value
    ///     type) on the wire, so the host installs its JSON converters even where nothing declared
    ///     i18n otherwise. Without them such a request is a 400 before any rule runs.
    /// </summary>
    public const string I18nWireTypes = "31";

    /// <summary>
    ///     PdxTemplates = 32 — an assembly embeds document or mail templates and says so with
    ///     <c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c>; the host registers it as a template source.
    /// </summary>
    public const string PdxTemplates = "32";

    /// <summary>
    ///     OutputCache = 33 — an assembly declares a shared <c>[ResponseCache]</c>; the host adds the
    ///     output cache services and middleware without which it keeps nothing.
    /// </summary>
    public const string OutputCache = "33";
}
