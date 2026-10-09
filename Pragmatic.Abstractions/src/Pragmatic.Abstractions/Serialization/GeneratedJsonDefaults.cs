using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Serialization;

/// <summary>
///     Whether a generated response writer writes, under a host's response options, what the serializer would: the
///     options are the ones the generated entry point wrote, and nothing the host registered besides claims a type the
///     writer writes.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, not meant to be called by hand. The entry point marks the options at the end
///         of the configuration it writes; a generated endpoint asks, when it answers, whether its writer still holds.
///     </para>
///     <para>
///         <b>Why at run time.</b> The writers are fixed at compile time, but the options are not: an
///         <c>IStartupStep</c> or the application's own <c>Configure&lt;JsonOptions&gt;</c> can change the naming
///         policy, add a converter or replace the resolver after the entry point ran, and that is ordinary code the
///         generator cannot read. So the entry point records what it wrote, and anything different, in any setting
///         that changes the bytes, sends the response back to the serializer.
///     </para>
///     <para>
///         <b>What every writer assumes</b>, checked when the options are marked: camelCase names and nothing for
///         dictionary keys, nulls left out, cycles ignored, ASP.NET's encoder (<see cref="ResponseEncoder" />), no
///         indentation, numbers as numbers, no fields, read-only properties written, no nullable annotations enforced,
///         and the entry point's <c>JsonStringEnumConverter</c> last in the list.
///     </para>
///     <para>
///         <b>What each writer is asked</b>, once while the options stay the same, for the types its
///         <see cref="GeneratedJsonShape" /> names: that no converter claims one (Internationalization registers its
///         own for <c>Money</c>, Temporal for its dates), that no modifier on the seam says it touches one (a modifier
///         that does not say touches all of them), and that no resolver in the chain besides the entry point's answers
///         for one (ASP.NET's OpenAPI puts its schema context first, and it answers only for its own types).
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedJsonDefaults
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, Snapshot> Marked = new();

    /// <summary>
    ///     The encoder a response is escaped with: the one ASP.NET's <c>JsonOptions</c> start from, which leaves
    ///     non-ASCII text and the HTML-sensitive characters as they are, since a response body is not embedded in a page.
    /// </summary>
    /// <remarks>
    ///     Not System.Text.Json's default, which escapes both. The generated writers write their names and text with
    ///     this one, and are used only where the options still carry it.
    /// </remarks>
    public static System.Text.Encodings.Web.JavaScriptEncoder ResponseEncoder
        => System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>
    ///     The options a generated response writer is called with: the response encoder, and no validation.
    /// </summary>
    /// <remarks>
    ///     The writer is generated from the type, so its shape is right by construction, and it relies on that: a run
    ///     of members (<see cref="Utf8JsonRun" />) starts with a name where a validating writer expects one, and is
    ///     refused there. <c>GeneratedJsonResponse</c> writes with these, and so does anything that calls a writer to
    ///     compare it with the serializer.
    /// </remarks>
    public static JsonWriterOptions ResponseWriterOptions => new()
    {
        Encoder = ResponseEncoder,
        SkipValidation = true,
    };

    /// <summary>A property name or an enum name, encoded once as a response writes it.</summary>
    public static JsonEncodedText Encode(string text) => JsonEncodedText.Encode(text, ResponseEncoder);

    /// <summary>Records <paramref name="options" /> as the configuration the generated entry point wrote.</summary>
    /// <param name="options">The host's response options, as the entry point left them.</param>
    /// <param name="seam">The shared seam the entry point took the resolver from.</param>
    /// <param name="excludesInfrastructure">Whether the entry point wrapped the resolver in the infrastructure modifier.</param>
    public static void Mark(JsonSerializerOptions options, PragmaticJsonOptions seam, bool excludesInfrastructure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(seam);

        Marked.AddOrUpdate(options, new Snapshot(options, seam, WhatEveryWriterAssumes(options), excludesInfrastructure));
    }

    /// <summary>
    ///     Whether the generated response writer of <paramref name="shape" /> writes, under
    ///     <paramref name="options" />, what the serializer would.
    /// </summary>
    /// <param name="options">The host's response options, as they are now.</param>
    /// <param name="shape">What the writer writes.</param>
    public static bool AllowGeneratedWriters(JsonSerializerOptions options, GeneratedJsonShape shape)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(shape);

        if (!Marked.TryGetValue(options, out var snapshot) || !snapshot.Assumed)
            return false;

        if (shape.NeedsInfrastructureExclusion && !snapshot.ExcludesInfrastructure)
            return false;

        return snapshot.Matches(options) && snapshot.Covers(options, shape);
    }

    private static bool WhatEveryWriterAssumes(JsonSerializerOptions options)
        => ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.CamelCase)
           && options.DictionaryKeyPolicy is null
           && options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull
           && ReferenceEquals(options.ReferenceHandler, ReferenceHandler.IgnoreCycles)
           && ReferenceEquals(options.Encoder, ResponseEncoder)
           && !options.WriteIndented
           && !options.IncludeFields
           && !options.IgnoreReadOnlyProperties
           && !options.IgnoreReadOnlyFields
           && !options.RespectNullableAnnotations
           && (options.NumberHandling & (JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowNamedFloatingPointLiterals)) == 0
           && options.MaxDepth is 0 or 64
           && options.Converters.Count > 0
           && options.Converters[^1].GetType() == typeof(JsonStringEnumConverter);

    /// <summary>The converter the serializer would use for <paramref name="type" />: the first that claims it.</summary>
    private static JsonConverter? FirstClaiming(JsonConverter[] converters, Type type)
    {
        foreach (var converter in converters)
            if (converter.CanConvert(type))
                return converter;

        return null;
    }

    /// <summary>Every setting that changes the bytes, as the entry point left it.</summary>
    private sealed class Snapshot
    {
        private readonly JsonNamingPolicy? _naming;
        private readonly JsonNamingPolicy? _dictionaryKeys;
        private readonly JsonIgnoreCondition _ignore;
        private readonly ReferenceHandler? _references;
        private readonly System.Text.Encodings.Web.JavaScriptEncoder? _encoder;
        private readonly bool _indented;
        private readonly bool _fields;
        private readonly bool _readOnlyProperties;
        private readonly bool _readOnlyFields;
        private readonly bool _nullableAnnotations;
        private readonly JsonNumberHandling _numbers;
        private readonly int _maxDepth;
        private readonly JsonConverter[] _converters;

        // The resolvers the entry point left in the chain, which it must still hold, and what the seam's modifiers say
        // they touch: their effects travel with those resolvers. ⚠️ The chain, not TypeInfoResolver: assigning a
        // combined resolver flattens it into the chain, so the object assigned is not an element of it.
        private readonly IJsonTypeInfoResolver[] _resolvers;
        private readonly Func<Type, bool>?[] _modifierScopes;

        // Once the options are read-only nothing can change them, so the answer is kept: 0 unknown, 1 yes, 2 no.
        private int _frozenAnswer;

        // The answer for each writer, with the resolver chain it was computed against.
        private readonly ConditionalWeakTable<GeneratedJsonShape, Covered> _covered = new();

        public Snapshot(JsonSerializerOptions options, PragmaticJsonOptions seam, bool assumed, bool excludesInfrastructure)
        {
            Assumed = assumed;
            ExcludesInfrastructure = excludesInfrastructure;
            _naming = options.PropertyNamingPolicy;
            _dictionaryKeys = options.DictionaryKeyPolicy;
            _ignore = options.DefaultIgnoreCondition;
            _references = options.ReferenceHandler;
            _encoder = options.Encoder;
            _indented = options.WriteIndented;
            _fields = options.IncludeFields;
            _readOnlyProperties = options.IgnoreReadOnlyProperties;
            _readOnlyFields = options.IgnoreReadOnlyFields;
            _nullableAnnotations = options.RespectNullableAnnotations;
            _numbers = options.NumberHandling;
            _maxDepth = options.MaxDepth;
            _converters = [.. options.Converters];
            _resolvers = [.. options.TypeInfoResolverChain];
            _modifierScopes = [.. seam.ModifierScopes];
        }

        public bool Assumed { get; }

        public bool ExcludesInfrastructure { get; }

        public bool Matches(JsonSerializerOptions options)
        {
            var frozen = Volatile.Read(ref _frozenAnswer);
            if (frozen != 0)
                return frozen == 1;

            var matches = Compare(options);
            if (options.IsReadOnly)
                Volatile.Write(ref _frozenAnswer, matches ? 1 : 2);

            return matches;
        }

        /// <summary>
        ///     Whether nothing the host registered besides the entry point's own claims a type the writer writes
        ///     itself, and each of its enums is claimed by the converter whose output it reproduces.
        /// </summary>
        public bool Covers(JsonSerializerOptions options, GeneratedJsonShape shape)
        {
            var chain = options.TypeInfoResolverChain;
            if (_covered.TryGetValue(shape, out var known) && known.SameChain(chain))
                return known.Answer;

            var answer = Compute(options, chain, shape);
            _covered.AddOrUpdate(shape, new Covered([.. chain], answer));
            return answer;
        }

        private bool Compute(JsonSerializerOptions options, IList<IJsonTypeInfoResolver> chain, GeneratedJsonShape shape)
        {
            foreach (var type in shape.Types)
            {
                if (FirstClaiming(_converters, type) is not null)
                    return false;

                foreach (var touches in _modifierScopes)
                    if (touches is null || touches(type))
                        return false;

                // Another resolver ahead of, or beside, the entry point's would answer for the type with metadata of
                // its own. One that knows nothing of it (OpenAPI's schema context) returns null and changes nothing.
                foreach (var resolver in chain)
                    if (!IsOurs(resolver) && resolver.GetTypeInfo(type, options) is not null)
                        return false;
            }

            // Assumed means the entry point's enum converter is the last one: the one every enum falls through to.
            var enumConverter = _converters[^1];
            for (var i = 0; i < shape.Enums.Count; i++)
            {
                var claiming = FirstClaiming(_converters, shape.Enums[i]);
                var reproduced = ReferenceEquals(claiming, enumConverter)
                                 || (shape.EnumConverters[i] is { } fastEnum && claiming?.GetType() == fastEnum);
                if (!reproduced)
                    return false;
            }

            return true;
        }

        private bool Compare(JsonSerializerOptions options)
        {
            if (!ReferenceEquals(options.PropertyNamingPolicy, _naming)
                || !ReferenceEquals(options.DictionaryKeyPolicy, _dictionaryKeys)
                || options.DefaultIgnoreCondition != _ignore
                || !ReferenceEquals(options.ReferenceHandler, _references)
                || !ReferenceEquals(options.Encoder, _encoder)
                || options.WriteIndented != _indented
                || options.IncludeFields != _fields
                || options.IgnoreReadOnlyProperties != _readOnlyProperties
                || options.IgnoreReadOnlyFields != _readOnlyFields
                || options.RespectNullableAnnotations != _nullableAnnotations
                || options.NumberHandling != _numbers
                || options.MaxDepth != _maxDepth
                || options.Converters.Count != _converters.Length
                || _resolvers.Length == 0)
                return false;

            for (var i = 0; i < _converters.Length; i++)
                if (!ReferenceEquals(options.Converters[i], _converters[i]))
                    return false;

            // Ours still answer, in the order the entry point left them; what was put around them is asked per writer.
            var chain = options.TypeInfoResolverChain;
            var next = 0;
            foreach (var resolver in chain)
                if (next < _resolvers.Length && ReferenceEquals(resolver, _resolvers[next]))
                    next++;

            return next == _resolvers.Length;
        }

        private bool IsOurs(IJsonTypeInfoResolver resolver)
        {
            foreach (var ours in _resolvers)
                if (ReferenceEquals(ours, resolver))
                    return true;

            return false;
        }
    }

    /// <summary>A writer's answer, and the resolver chain it holds for.</summary>
    private sealed class Covered(IJsonTypeInfoResolver[] chain, bool answer)
    {
        public bool Answer { get; } = answer;

        public bool SameChain(IList<IJsonTypeInfoResolver> current)
        {
            if (current.Count != chain.Length)
                return false;

            for (var i = 0; i < chain.Length; i++)
                if (!ReferenceEquals(current[i], chain[i]))
                    return false;

            return true;
        }
    }
}
