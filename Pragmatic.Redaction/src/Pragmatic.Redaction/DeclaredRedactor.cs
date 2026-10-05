using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Serialization;

namespace Pragmatic.Redaction;

/// <summary>
///     Redacts what a type <b>declared</b> must not be logged — <c>[NotLogged]</c> or
///     <c>[PersonalData]</c> — as opposed to what merely looks sensitive.
/// </summary>
/// <remarks>
///     <para>
///         The companion of <see cref="PersonalDataRedactor" />, and neither replaces the other. The
///         pattern redactor catches what nobody annotated, but only where the text has a recognisable
///         shape — an e-mail, an IBAN, a long digit run. This one catches what has no shape at all: an
///         internal identifier, a pricing coefficient, a token indistinguishable from any other
///         string. Those are invisible to every regex, because there is nothing to recognise.
///     </para>
///     <para>
///         The two are governed differently, and deliberately. Pattern redaction is a heuristic with
///         false positives and a readability cost, so it sits behind <c>Privacy.EnableRedaction</c>
///         and is off in development. <b>Declared redaction is not optional and has no environment
///         qualifier</b>: a member the developer marked is a contract, and "never in the logs" does
///         not mean "except on my machine".
///     </para>
///     <para>
///         It reads the compile-time maps the generator emits, so there is no reflection over the
///         payload. When no map knows the type the value is returned untouched: a type nobody
///         declared anything about has nothing to hide.
///     </para>
///     <para>
///         <b>How deep it goes, and where that is decided.</b> A map entry is a <em>path</em> —
///         <c>Member</c>, <c>Member.Inner</c>, <c>Member[].Inner</c> — and the generator wrote every
///         one of them by walking the type at compile time. Nothing here asks the payload what type a
///         node is, because JSON does not carry one: a redactor that wanted to descend on its own
///         would have to reflect over the object, which is the one thing this mechanism was built to
///         avoid.
///     </para>
///     <para>
///         ⚠️ A walk over the top-level key set, consulting the outermost type's map alone, would send
///         <b>anything classified inside an owned record or a collection out in clear</b> — the
///         framework's own <c>LocalIdentity</c> with the password hash, both bearer tokens and the
///         security stamp, beside an address the entity above it had masked. A reader who classifies a
///         field has no way to find out where such a guarantee stops, which is why the map carries
///         paths.
///     </para>
///     <para>
///         The depth limit and the cycle rule are therefore the generator's — see
///         <c>DeclaredRedactionPathTransform</c> — and cost nothing here: this walks the paths it was
///         given, and a path that does not match the payload ends quietly.
///     </para>
/// </remarks>
public sealed class DeclaredRedactor
{
    private readonly IRedactionMap[] _maps;
    private readonly JsonSerializerOptions _options;
    private long _valuesWithoutMetadata;

    /// <param name="maps">The generated maps, and any an application contributed.</param>
    /// <remarks>
    ///     <para>
    ///         A value is serialized from the JSON metadata its map carries
    ///         (<see cref="IRedactionMap.TypeInfoResolver" />): the generated context, which covers every
    ///         type the map knows when the assembly emits one. After the maps comes the framework's own
    ///         resolver chain, which on a JIT runtime ends in reflection and under Native AOT does not.
    ///     </para>
    ///     <para>
    ///         Names are camelCase, the policy the JSON providers write every other complex value with,
    ///         and the one the generated context bakes in. With any other policy the same value would be
    ///         written one way by a JIT host and another by the same host published AOT.
    ///     </para>
    /// </remarks>
    public DeclaredRedactor(IEnumerable<IRedactionMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps as IRedactionMap[] ?? [.. maps];

        var resolvers = new List<IJsonTypeInfoResolver>();
        foreach (var map in _maps)
        {
            if (map.TypeInfoResolver is { } resolver && !resolvers.Contains(resolver))
                resolvers.Add(resolver);
        }

        if (new PragmaticJsonOptions().Build().TypeInfoResolver is { } framework)
            resolvers.Add(framework);

        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = JsonTypeInfoResolver.Combine([.. resolvers]),
        };
    }

    /// <summary>True when no map was contributed, so every call is a no-op.</summary>
    public bool IsEmpty => _maps.Length == 0;

    /// <summary>
    ///     How many values had declared members and could not be serialized: no JSON metadata for their
    ///     type under Native AOT. Each one was written as the mask, whole, rather than lost or sent out.
    /// </summary>
    public long ValuesWithoutMetadata => Interlocked.Read(ref _valuesWithoutMetadata);

    /// <summary>
    ///     The value as it may be written out: unchanged when its type declared nothing, and the
    ///     redacted JSON otherwise.
    /// </summary>
    /// <remarks>
    ///     Returns a <see cref="string" /> for a redacted value, which every provider renders the
    ///     same way — the JSON writers already emit a complex property as a string, and the console
    ///     and debug writers call <c>ToString()</c>. That is the point of doing this once, on the
    ///     entry, rather than in each provider: a provider added tomorrow cannot forget.
    /// </remarks>
    public object? RedactValue(object? value)
    {
        if (value is null || value is string || value.GetType().IsPrimitive)
            return value;

        return TryGetRedactedMembers(value.GetType(), out _) ? Serialize(value) : value;
    }

    /// <summary>
    ///     A log entry's structured state with every value whose type declared members masked, or null
    ///     when no value declared anything.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The returned list renders the message from the masked values (its <c>ToString()</c>), and
    ///         that rendering is the message to write. The caller's formatter closes over the original
    ///         state, so calling it would put the clear value back into the text while the structured
    ///         property beside it was masked.
    ///     </para>
    ///     <para>
    ///         Null, not the original list, when nothing changed: the common entry declares nothing,
    ///         allocates nothing, and keeps the message its own formatter renders.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, object?>>? RedactState(IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        if (IsEmpty)
            return null;

        KeyValuePair<string, object?>[]? masked = null;

        for (var i = 0; i < values.Count; i++)
        {
            var original = values[i].Value;
            var redacted = RedactValue(original);

            if (ReferenceEquals(redacted, original))
                continue;

            // Copy on first difference.
            masked ??= ToArray(values);
            masked[i] = new KeyValuePair<string, object?>(values[i].Key, redacted);
        }

        return masked is null ? null : new RedactedLogValues(masked);
    }

    private static KeyValuePair<string, object?>[] ToArray(IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        var copy = new KeyValuePair<string, object?>[values.Count];
        for (var i = 0; i < values.Count; i++)
            copy[i] = values[i];

        return copy;
    }

    /// <summary>
    ///     Serializes <paramref name="value" /> with every declared member replaced by
    ///     <see cref="PersonalDataPatterns.Mask" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Masks rather than omits, deliberately: a missing key reads as "the field was not set",
    ///         which is a different statement about what happened, and a false one.
    ///     </para>
    ///     <para>
    ///         ⚠️ A value whose type has no JSON metadata (Native AOT, an assembly that emits no generated
    ///         context, a member type the context cannot describe) is written as the mask whole and
    ///         counted in <see cref="ValuesWithoutMetadata" />. Throwing here is what used to happen: the
    ///         exception landed in the provider's catch, and the entry carrying the value was lost.
    ///     </para>
    /// </remarks>
    public string Serialize(object? value)
    {
        if (value is null)
            return "null";

        var type = value.GetType();
        string json;
        try
        {
            if (!_options.TryGetTypeInfo(type, out var typeInfo))
                return WithoutMetadata(type);

            json = JsonSerializer.Serialize(value, typeInfo);
        }
        catch (NotSupportedException)
        {
            // Metadata for the type, none for one of its members: the serializer says so only here.
            return WithoutMetadata(type);
        }

        if (!TryGetRedactedMembers(type, out var members))
            return json;

        if (JsonNode.Parse(json) is not JsonObject obj)
            return json;

        var masked = false;
        foreach (var member in members)
            masked |= MaskPath(obj, member.Name);

        return masked ? obj.ToJsonString() : json;
    }

    private string WithoutMetadata(Type type)
    {
        Interlocked.Increment(ref _valuesWithoutMetadata);
        return $"{PersonalDataPatterns.Mask} ({type.Name}: no JSON metadata)";
    }

    /// <summary>
    ///     Masks every node <paramref name="path" /> reaches, and answers whether it reached any.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A path is <c>Member</c>, <c>Member.Inner</c> or <c>Member[].Inner</c>: the segments the
    ///         generator wrote, walked here without asking the payload what type anything is. That is
    ///         the whole reason the depth lives in the map — see the class remarks.
    ///     </para>
    ///     <para>
    ///         Segments are matched case-insensitively for the reason a single name was: without
    ///         <c>[JsonPropertyName]</c> the map carries the CLR name in PascalCase while the payload
    ///         may well be camelCase.
    ///     </para>
    ///     <para>
    ///         ⚠️ A segment the payload does not have simply ends the walk. This runs inside the
    ///         logger, where throwing would lose the entry it was called to protect — and a payload
    ///         that does not match is ordinary: a projection, a null branch, a polymorphic member
    ///         serialized as something else.
    ///     </para>
    /// </remarks>
    private static bool MaskPath(JsonNode? node, string path)
    {
        var separator = path.IndexOf('.');
        var segment = separator < 0 ? path : path[..separator];
        var rest = separator < 0 ? null : path[(separator + 1)..];

        var overElements = segment.EndsWith("[]", StringComparison.Ordinal);
        if (overElements)
            segment = segment[..^2];

        if (node is not JsonObject obj)
            return false;

        var key = obj.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, segment, StringComparison.OrdinalIgnoreCase));
        if (key is null)
            return false;

        if (overElements)
        {
            if (obj[key] is not JsonArray elements)
                return false;

            var maskedAny = false;
            foreach (var element in elements)
                maskedAny |= rest is null ? Replace(element) : MaskPath(element, rest);

            return maskedAny;
        }

        if (rest is not null)
            return MaskPath(obj[key], rest);

        // Masks rather than omits, for the reason Serialize's remark gives.
        obj[key] = PersonalDataPatterns.Mask;
        return true;
    }

    /// <summary>Replaces an array element in place, which needs its index rather than its key.</summary>
    private static bool Replace(JsonNode? element)
    {
        if (element?.Parent is not JsonArray array)
            return false;

        var index = array.IndexOf(element);
        if (index < 0)
            return false;

        array[index] = PersonalDataPatterns.Mask;
        return true;
    }

    /// <summary>The members <paramref name="type" /> declared, from whichever map knows the type.</summary>
    public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
    {
        foreach (var map in _maps)
        {
            if (map.TryGetRedactedMembers(type, out var found) && found.Count > 0)
            {
                members = found;
                return true;
            }
        }

        members = [];
        return false;
    }
}
