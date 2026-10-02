using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Serialization;

/// <summary>
///     Central seam for JSON serialization across the Pragmatic ecosystem.
///     Holds an ordered chain of source-generated <see cref="IJsonTypeInfoResolver"/> contexts
///     plus an opt-out reflection fallback, and builds the shared <see cref="JsonSerializerOptions"/>
///     consumed by every framework serialization boundary (messaging, outbox, sagas, jobs, host).
/// </summary>
/// <remarks>
///     <para>
///         The reflection fallback is <b>enabled by default on a JIT runtime</b> so existing consumers
///         keep working with zero changes, and <b>off under Native AOT</b>. Registering the
///         source-generated contexts is therefore not optional in an AOT publish; turn the fallback off
///         explicitly too when you want the same failure mode while developing on the JIT:
///     </para>
///     <code>
/// app.UseJson(json =>
/// {
///     json.AddContext(AppJsonContext.Default);   // your [JsonSerializable] context
///     json.DisableReflectionFallback();          // AOT: no reflection-based resolver
/// });
///     </code>
///     <para>
///         Framework packages contribute their own hand-written contexts (see the AOT plan),
///         and the host analyzer flags user boundary types missing from the registered contexts.
///     </para>
/// </remarks>
public sealed class PragmaticJsonOptions
{
    private readonly List<IJsonTypeInfoResolver> _contexts = [PragmaticCommonJsonContext.Default];
    private readonly List<JsonConverter> _converters = [];
    private readonly List<Action<JsonTypeInfo>> _modifiers = [];
    // Guards _contexts/_converters/_built: configuration happens at startup, but Build() runs on the
    // first serialization — without the lock a concurrent AddContext could mutate the list mid-copy.
    private readonly Lock _lock = new();
    private JsonSerializerOptions? _built;

    /// <summary>
    ///     A shared default instance used by components constructed outside DI — e.g. in tests or
    ///     manually-wired scenarios. Reflection fallback enabled; contexts are the seeded baseline
    ///     only (see <see cref="Contexts"/>), with nothing registered by the application.
    /// </summary>
    public static PragmaticJsonOptions Default { get; } = new();

    /// <summary>
    ///     Whether a reflection-based resolver is appended to the chain as a catch-all.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see langword="true"/> on a JIT runtime, and to <see langword="false"/> under
    ///     Native AOT or when the <c>JsonSerializerIsReflectionEnabledByDefault</c> feature switch is off.
    ///     <para>
    ///         Derived rather than constant because the suppressions further down justify themselves
    ///         with "AOT publishes turn it off". As a constant that would be an assumption about the
    ///         caller and nothing more: an AOT publish that forgot <see cref="DisableReflectionFallback"/>
    ///         would get a reflective resolver with the warning already suppressed — the exact silent
    ///         fallback this type exists to prevent. The runtime answers the question instead of the
    ///         caller.
    ///     </para>
    /// </remarks>
    public bool ReflectionFallbackEnabled { get; private set; } =
        RuntimeFeature.IsDynamicCodeSupported && JsonSerializer.IsReflectionEnabledByDefault;

    /// <summary>
    ///     The registered source-generated contexts, in resolution order (before the reflection
    ///     fallback). Includes the seeded <see cref="PragmaticCommonJsonContext"/> baseline.
    /// </summary>
    public IReadOnlyList<IJsonTypeInfoResolver> Contexts => _contexts;

    /// <summary>
    ///     Registers a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> (pass its
    ///     <c>Default</c> instance) into the resolver chain. Contexts are consulted in
    ///     registration order, before the reflection fallback.
    /// </summary>
    public PragmaticJsonOptions AddContext(IJsonTypeInfoResolver context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_lock)
        {
            EnsureNotBuilt();
            _contexts.Add(context);
        }

        return this;
    }

    /// <summary>
    ///     Adds a context only if the same instance is not already registered. Used by framework
    ///     packages whose registration may run more than once (TryAdd semantics), so duplicate
    ///     <c>Default</c> singletons don't stack up in the resolver chain.
    /// </summary>
    public PragmaticJsonOptions AddContextOnce(IJsonTypeInfoResolver context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_lock)
        {
            EnsureNotBuilt();
            if (!_contexts.Contains(context))
                _contexts.Add(context);
        }

        return this;
    }

    /// <summary>
    ///     Registers a converter on the shared options.
    /// </summary>
    /// <param name="converter">The converter. Typed, not a factory.</param>
    /// <remarks>
    ///     Exists so the generated result converters have a registration point that is not a line the
    ///     application has to remember to write. A converter is not a resolver, so <see cref="AddContext" />
    ///     could not carry them.
    ///     <para>
    ///         Deliberately typed <see cref="JsonConverter" /> rather than anything factory-shaped: a
    ///         <c>JsonConverterFactory</c> decides at run time which converter a type needs, which is the
    ///         reflection this API replaced.
    ///     </para>
    /// </remarks>
    public PragmaticJsonOptions AddConverter(JsonConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        lock (_lock)
        {
            EnsureNotBuilt();
            if (!_converters.Contains(converter))
                _converters.Add(converter);
        }

        return this;
    }

    /// <summary>
    ///     The registered converters, in registration order.
    /// </summary>
    public IReadOnlyList<JsonConverter> Converters => _converters;

    /// <summary>
    ///     Registers a <see cref="JsonTypeInfo" /> modifier on the shared options.
    /// </summary>
    /// <param name="modifier">The modifier, applied to every type the resolver is asked for.</param>
    /// <remarks>
    ///     <para>
    ///         The seam a contributor needs in order to change how a property is written without owning
    ///         the resolver. Before it, the only way was to wrap whatever resolver happened to be on a
    ///         <see cref="JsonSerializerOptions" /> at that moment — and whoever assigned a resolver
    ///         afterwards discarded the wrap. That failure is silent by construction: a modifier that
    ///         does not run produces a well-formed payload with the wrong values.
    ///     </para>
    ///     <para>
    ///         Modifiers <b>compose</b>: every registered one is applied, in registration order, on top
    ///         of the resolver this class builds. Registering is therefore additive and order-independent
    ///         for modifiers that touch different properties.
    ///     </para>
    /// </remarks>
    public PragmaticJsonOptions AddModifier(Action<JsonTypeInfo> modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        lock (_lock)
        {
            EnsureNotBuilt();
            if (!_modifiers.Contains(modifier))
                _modifiers.Add(modifier);
        }

        return this;
    }

    /// <summary>
    ///     The registered modifiers, in registration order.
    /// </summary>
    public IReadOnlyList<Action<JsonTypeInfo>> Modifiers => _modifiers;

    /// <summary>
    ///     Turns off the reflection-based catch-all resolver. Required for a fully AOT-safe
    ///     serialization pipeline: after this, every serialized type must be covered by a
    ///     registered context or serialization throws at runtime.
    /// </summary>
    public PragmaticJsonOptions DisableReflectionFallback()
    {
        lock (_lock)
        {
            EnsureNotBuilt();
            ReflectionFallbackEnabled = false;
        }

        return this;
    }

    /// <summary>
    ///     Builds (and caches) the shared, read-only <see cref="JsonSerializerOptions"/>:
    ///     camelCase naming, the registered contexts, and — unless disabled — a reflection fallback.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "The reflection fallback defaults off under Native AOT (RuntimeFeature.IsDynamicCodeSupported) and is opt-out on the JIT via DisableReflectionFallback(), so an AOT publish resolves solely through registered source-generated contexts.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "The reflection fallback defaults off under Native AOT (RuntimeFeature.IsDynamicCodeSupported) and is opt-out on the JIT via DisableReflectionFallback(), so an AOT publish resolves solely through registered source-generated contexts.")]
    public JsonSerializerOptions Build()
    {
        if (_built is not null)
            return _built;

        lock (_lock)
        {
            if (_built is not null)
                return _built;

            return _built = BuildCore();
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "The reflection fallback defaults off under Native AOT (RuntimeFeature.IsDynamicCodeSupported) and is opt-out on the JIT via DisableReflectionFallback(), so an AOT publish resolves solely through registered source-generated contexts.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "The reflection fallback defaults off under Native AOT (RuntimeFeature.IsDynamicCodeSupported) and is opt-out on the JIT via DisableReflectionFallback(), so an AOT publish resolves solely through registered source-generated contexts.")]
    private JsonSerializerOptions BuildCore()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        foreach (var converter in _converters)
            options.Converters.Add(converter);

        var resolvers = new List<IJsonTypeInfoResolver>(_contexts);
        if (ReflectionFallbackEnabled)
            resolvers.Add(new DefaultJsonTypeInfoResolver());

        if (resolvers.Count > 0)
        {
            var resolver = resolvers.Count == 1
                ? resolvers[0]
                : JsonTypeInfoResolver.Combine([.. resolvers]);

            // Every registered modifier, on top of the composed resolver and before it is frozen. This
            // is the only place a modifier is attached, which is the point: a contributor that wrapped
            // the options itself would be discarded by whoever assigned a resolver next, silently.
            foreach (var modifier in _modifiers)
                resolver = resolver.WithAddedModifier(modifier);

            options.TypeInfoResolver = resolver;
            options.MakeReadOnly();
        }

        return options;
    }

    private void EnsureNotBuilt()
    {
        if (_built is not null)
            throw new InvalidOperationException(
                "PragmaticJsonOptions has already been built and can no longer be modified. " +
                "Configure all contexts via UseJson(...) before the application starts serializing.");
    }
}
