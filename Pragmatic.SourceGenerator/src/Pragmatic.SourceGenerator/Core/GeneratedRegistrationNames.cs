// Pragmatic.SourceGenerator - Core - Names of the generated registration entry points

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The single place where the name of every generated registration entry point the Composition
///     host calls lives.
/// </summary>
/// <remarks>
///     <para>
///         Each of these names is a contract between two files that never see each other: the template
///         that <i>renders</i> the class and method, and whatever tells the host to <i>call</i> it — the
///         metadata attribute for a referenced assembly, or the local-registration channel for types the
///         host declares itself. Spelling the name in both places is how a registration silently stops
///         being invoked, so both sides read it from here.
///     </para>
///     <para>
///         Changing a value here changes public generated API. It is deliberately not derived from
///         anything: grep for the constant and every side of the contract shows up.
///     </para>
/// </remarks>
internal static class GeneratedRegistrationNames
{
    /// <summary>Sub-namespace of the registrations that live under <c>{AssemblyName}.Generated</c>.</summary>
    public const string GeneratedNamespace = "Generated";

    // ── Validation ────────────────────────────────────────────────────────────────────────────────
    public const string ValidatorsClass = "ValidatorRegistrationExtensions";
    public const string ValidatorsMethod = "AddGeneratedValidators";

    // ── Identity / Authorization ──────────────────────────────────────────────────────────────────
    public const string AuthorizationClass = "AuthorizationRegistrationExtensions";
    public const string AuthorizationMethod = "AddGeneratedAuthorizationCatalog";

    // ── Actions: auto-derived permission catalog ──────────────────────────────────────────────────
    // Separate from the Identity one on purpose: both feed the same DI-aggregated catalog, and the host
    // calls every distinct registration method it finds under the Authorization category.
    public const string ActionPermissionCatalogClass = "ActionPermissionCatalogRegistration";
    public const string ActionPermissionCatalogMethod = "AddGeneratedActionPermissionCatalog";

    // ── Actions / Mutations: the invoker registrations a host calls ─────────────────────────
    // The class and the method carry the **first segment** of the assembly's namespace prefix, not the
    // module name: Conformance.Sales produces ConformanceActionsRegistrationExtensions.AddConformanceActions.
    // Shared by the templates that emit them and by the metadata that tells the host to call them. A
    // host that re-derived this rule would get it wrong, and the failure lands in a generated file.
    public const string ActionsClassSuffix = "ActionsRegistrationExtensions";
    public const string MutationsClassSuffix = "MutationsRegistrationExtensions";

    /// <summary>Namespace of the registration extensions when the assembly declares no namespace of its own.</summary>
    public const string ActionsFallbackNamespace = "Pragmatic.Actions.Generated";

    // ── Jobs ──────────────────────────────────────────────────────────────────────────────────────
    public const string JobsClass = "PragmaticJobRegistration";
    public const string JobsMethod = "AddDiscoveredJobs";

    // ── Messaging ─────────────────────────────────────────────────────────────────────────────────
    public const string MessageHandlersClass = "PragmaticMessageHandlerRegistration";
    public const string MessageHandlersMethod = "AddPragmaticMessageHandlers";

    // ── Serialization ─────────────────────────────────────────────────────────────────────────────
    public const string JsonContextClass = "PragmaticJsonContextRegistration";
    public const string JsonContextMethod = "AddGeneratedJsonContext";

    // ── Persistence: roll-up rules ────────────────────────────────────────────────────────────────
    // Not a partial hook inside AddPragmaticPersistenceRepositories: that is an entry point the host
    // path never calls, so the interceptor would ask the container for rules, get an empty list, and
    // every [RollUp] would be inert. A uniquely named public method is what a host can call — a
    // partial in a class every module names identically is not.
    public const string RollUpRulesClass = "PragmaticRollUpRuleRegistration";
    public const string RollUpRulesMethod = "AddGeneratedRollUpRules";

    // ── FastEnum ──────────────────────────────────────────────────────────────────────────────────
    public const string FastEnumConvertersClass = "PragmaticFastEnumJsonConverters";
    public const string FastEnumConvertersMethod = "AddTo";

    // ── Temporal ──────────────────────────────────────────────────────────────────────────────────
    // Unlike the ones above, the Temporal names carry the declaring namespace's prefix identifier:
    // the class is per-namespace, not per-assembly.
    public const string TemporalBehaviorsClassSuffix = "TemporalBehaviorExtensions";
    public const string TemporalBehaviorsMethodSuffix = "TemporalBehaviors";

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's validators.</summary>
    public static string ValidatorsFqn(string namespacePrefix)
        => Compose(namespacePrefix, ValidatorsClass, ValidatorsMethod);

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's permission catalog.</summary>
    public static string AuthorizationFqn(string registryNamespace)
        => Compose(registryNamespace, AuthorizationClass, AuthorizationMethod);

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's derived action permissions.</summary>
    public static string ActionPermissionCatalogFqn(string rootNamespace)
        => Compose(InGeneratedNamespace(rootNamespace), ActionPermissionCatalogClass, ActionPermissionCatalogMethod);

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's jobs.</summary>
    public static string JobsFqn(string rootNamespace)
        => Compose(rootNamespace, JobsClass, JobsMethod);

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's message handlers.</summary>
    public static string MessageHandlersFqn(string registrationNamespace)
        => Compose(registrationNamespace, MessageHandlersClass, MessageHandlersMethod);

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's JSON context.</summary>
    public static string JsonContextFqn(string rootNamespace)
        => Compose(InGeneratedNamespace(rootNamespace), JsonContextClass, JsonContextMethod);

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's roll-up rules.</summary>
    public static string RollUpRulesFqn(string rootNamespace)
        => Compose(InGeneratedNamespace(rootNamespace), RollUpRulesClass, RollUpRulesMethod);

    /// <summary>The class that registers an assembly's redaction map.</summary>
    /// <remarks>
    ///     Shared by the template that emits it and the metadata that tells the host to call it. They
    ///     were two independent computations of the same name, which is one drift away from a host
    ///     calling a method that does not exist.
    /// </remarks>
    public static string RedactionRegistrationClass(string assemblyName)
        => NamingHelper.AppendSuffix(
            string.IsNullOrEmpty(assemblyName) ? "Pragmatic" : assemblyName.Replace(".", ""),
            "RedactionRegistrationExtensions");

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's redaction map.</summary>
    public static string RedactionFqn(string mapNamespace, string assemblyName)
        => Compose(mapNamespace, RedactionRegistrationClass(assemblyName), "AddGeneratedRedactionMap");

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's [FastEnum] converters.</summary>
    public static string FastEnumConvertersFqn(string rootNamespace)
        => Compose(InGeneratedNamespace(rootNamespace), FastEnumConvertersClass, FastEnumConvertersMethod);

    /// <summary>The class name of the timezone behaviour registration for a namespace prefix identifier.</summary>
    public static string TemporalBehaviorsClass(string prefixIdentifier)
        => prefixIdentifier + TemporalBehaviorsClassSuffix;

    /// <summary>The method name of the timezone behaviour registration for a namespace prefix identifier.</summary>
    public static string TemporalBehaviorsMethod(string prefixIdentifier)
        => "Add" + prefixIdentifier + TemporalBehaviorsMethodSuffix;

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's timezone behaviours.</summary>
    public static string TemporalBehaviorsFqn(string targetNamespace, string prefixIdentifier)
        => Compose(targetNamespace, TemporalBehaviorsClass(prefixIdentifier), TemporalBehaviorsMethod(prefixIdentifier));

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's action invokers.</summary>
    /// <param name="namespacePrefix">
    ///     The common namespace prefix of the assembly's operations — <c>Conformance.Sales</c>, not
    ///     <c>Conformance</c>. Empty when they share none.
    /// </param>
    public static string ActionsFqn(string namespacePrefix)
        => Compose(
            RegistrationNamespace(namespacePrefix),
            NamespacePrefixHelper.ToIdentifier(namespacePrefix) + ActionsClassSuffix,
            "Add" + NamespacePrefixHelper.ToIdentifier(namespacePrefix) + "Actions");

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host calls for this assembly's mutation invokers.</summary>
    /// <param name="namespacePrefix">As in <see cref="ActionsFqn" />.</param>
    public static string MutationsFqn(string namespacePrefix)
        => Compose(
            RegistrationNamespace(namespacePrefix),
            NamespacePrefixHelper.ToIdentifier(namespacePrefix) + MutationsClassSuffix,
            "Add" + NamespacePrefixHelper.ToIdentifier(namespacePrefix) + "Mutations");

    /// <summary>Where the two registration extensions above are declared.</summary>
    public static string RegistrationNamespace(string namespacePrefix)
        => string.IsNullOrEmpty(namespacePrefix) ? ActionsFallbackNamespace : namespacePrefix;

    /// <summary><c>{root}.Generated</c>, or <c>Generated</c> when the assembly has no root namespace.</summary>
    public static string InGeneratedNamespace(string rootNamespace)
        => string.IsNullOrEmpty(rootNamespace) ? GeneratedNamespace : rootNamespace + "." + GeneratedNamespace;

    private static string Compose(string containingNamespace, string className, string methodName)
        => string.IsNullOrEmpty(containingNamespace)
            ? className + "." + methodName
            : containingNamespace + "." + className + "." + methodName;
}
