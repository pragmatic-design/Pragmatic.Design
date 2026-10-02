using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     The <c>components.schemas</c> half of the OpenAPI document: which key each manifest type gets,
///     and how each schema is rendered. Split from the paths half for size only — the two are one unit,
///     because a <c>$ref</c> written while rendering a path has to name the key written here.
/// </summary>
internal static partial class OpenApiJsonGenerator
{
    /// <summary>Generic wrappers whose JSON payload is a plain array of the item type.</summary>
    private static readonly HashSet<string> CollectionWrappers = new(StringComparer.Ordinal)
    {
        "IReadOnlyList", "IReadOnlyCollection", "IList", "ICollection", "IEnumerable",
        "List", "HashSet", "ISet", "IReadOnlySet", "ImmutableArray", "ImmutableList"
    };

    /// <summary>Generic wrappers whose JSON payload is an object keyed by the first type argument.</summary>
    private static readonly HashSet<string> DictionaryWrappers = new(StringComparer.Ordinal)
    {
        "Dictionary", "IDictionary", "IReadOnlyDictionary", "SortedDictionary",
        "ImmutableDictionary", "ImmutableSortedDictionary"
    };

    /// <summary>A resolved response schema: the key to <c>$ref</c>, and whether it is wrapped in an array.</summary>
    private readonly struct ResponseSchemaRef
    {
        public ResponseSchemaRef(string name, bool isArray)
        {
            Name = name;
            IsArray = isArray;
        }

        public string Name { get; }
        public bool IsArray { get; }
    }

    /// <summary>
    ///     Owns the mapping "manifest type → <c>components.schemas</c> key" for the whole document.
    ///     <para>
    ///         Schema keys are JSON object keys, so they have to be unique: two modules declaring a DTO
    ///         with the same simple name — or a module declaring one called <c>ProblemDetails</c> — used
    ///         to emit two members under one key. The document still parsed, but every reader kept one of
    ///         them arbitrarily. The first declaration keeps the simple name and any later homonym falls
    ///         back to its fully-qualified name, which is unique by construction and stable across builds
    ///         because the manifest order is (assembly order, then declaration order).
    ///     </para>
    /// </summary>
    private sealed class SchemaCatalog
    {
        private readonly Dictionary<string, string> _byFqn = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _bySimpleName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pagedEnvelopes = new(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, string>> _pagedEnvelopeOrder = [];
        private readonly List<KeyValuePair<string, TypeDto>> _entries = [];
        private readonly HashSet<string> _used = new(StringComparer.Ordinal) { ProblemDetailsSchema };

        /// <summary>Schema key → type, in emission order.</summary>
        public IReadOnlyList<KeyValuePair<string, TypeDto>> Entries => _entries;

        /// <summary>Paged envelope key → item schema key, in first-use order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> PagedEnvelopes => _pagedEnvelopeOrder;

        public static SchemaCatalog Build(List<TypeDto> types, List<EndpointDto> endpoints)
        {
            var catalog = new SchemaCatalog();

            // Request-body schema names are reserved first so their $ref (derived from the operation id
            // at the call site) keeps resolving to the schema this generator actually emits; a DTO that
            // happens to share the name is the one that gets disambiguated.
            foreach (var ep in endpoints)
            {
                // Unconditionally, including for a directly-bound body: resolution is not possible yet
                // — the types go in below — and reserving a name that turns out unused costs nothing,
                // while failing to reserve one that is used lets a DTO take it.
                if (ep.RequestBody?.Properties is { Count: > 0 })
                    catalog._used.Add(RequestSchemaName(ep));
            }

            foreach (var t in types)
            {
                if (t.SimpleName is null) continue;
                // Error types are described by the shared ProblemDetails schema, never by one of their own.
                if (t.Kind == "error") continue;

                var identity = Normalize(t.Type) ?? t.SimpleName;
                if (catalog._byFqn.ContainsKey(identity)) continue; // same type declared by two modules

                var name = catalog.Reserve(t.SimpleName, identity);
                catalog._byFqn[identity] = name;
                if (!catalog._bySimpleName.ContainsKey(t.SimpleName))
                    catalog._bySimpleName[t.SimpleName] = name;
                catalog._entries.Add(new KeyValuePair<string, TypeDto>(name, t));
            }

            return catalog;
        }

        /// <summary>
        ///     Maps an endpoint response type to the schema it should reference, unwrapping the two
        ///     envelopes a <c>[Query]</c> endpoint answers with. Returns null when nothing in the
        ///     manifest describes the payload — an unresolvable <c>$ref</c> is worse than no schema.
        /// </summary>
        public ResponseSchemaRef? ResolveResponse(string? fqn)
        {
            var normalized = Normalize(fqn);
            if (normalized is null) return null;

            if (normalized.EndsWith("[]", StringComparison.Ordinal))
            {
                var element = Resolve(normalized.Substring(0, normalized.Length - 2));
                return element is null ? null : new ResponseSchemaRef(element, isArray: true);
            }

            var open = normalized.IndexOf('<');
            if (open < 0)
            {
                var direct = Resolve(normalized);
                return direct is null ? null : new ResponseSchemaRef(direct, isArray: false);
            }

            var close = normalized.LastIndexOf('>');
            if (close <= open) return null;

            var wrapper = SimpleNameOf(normalized.Substring(0, open));
            var item = Resolve(normalized.Substring(open + 1, close - open - 1));
            if (item is null) return null;

            if (wrapper == "PagedResult")
                return new ResponseSchemaRef(PagedEnvelopeFor(item), isArray: false);

            return CollectionWrappers.Contains(wrapper)
                ? new ResponseSchemaRef(item, isArray: true)
                // Any other generic wrapper is an unknown shape: pointing at the item would describe a
                // payload the endpoint does not send.
                : null;
        }

        /// <summary>
        ///     The schema key describing this type, or null when the document does not describe it.
        /// </summary>
        /// <remarks>
        ///     Property schemas need the same answer response schemas needed, and used not to ask: every
        ///     property whose type was not one of seven primitives was published as <c>"type": "string"</c>
        ///     by the fallback, including the ones whose type is a schema sitting in this very catalogue.
        /// </remarks>
        public string? ResolveSchema(string? fqn)
        {
            var normalized = Normalize(fqn);
            return normalized is null ? null : Resolve(normalized);
        }

        /// <summary>Resolves a type reference to its schema key: by FQN first, by simple name as fallback.</summary>
        private string? Resolve(string fqn)
        {
            var normalized = Normalize(fqn)!;
            if (_byFqn.TryGetValue(normalized, out var byFqn)) return byFqn;
            return _bySimpleName.TryGetValue(SimpleNameOf(normalized), out var bySimple) ? bySimple : null;
        }

        private string PagedEnvelopeFor(string itemSchemaName)
        {
            if (_pagedEnvelopes.TryGetValue(itemSchemaName, out var existing)) return existing;

            var preferred = "PagedResultOf" + itemSchemaName;
            var name = Reserve(preferred, preferred);
            _pagedEnvelopes[itemSchemaName] = name;
            _pagedEnvelopeOrder.Add(new KeyValuePair<string, string>(name, itemSchemaName));
            return name;
        }

        private string Reserve(string preferred, string identity)
        {
            if (_used.Add(preferred)) return preferred;

            // OpenAPI allows dots in a component key, so the FQN is a legal — and stable — fallback.
            var qualified = identity.Replace('+', '.');
            if (!string.Equals(qualified, preferred, StringComparison.Ordinal) && _used.Add(qualified))
                return qualified;

            for (var i = 2; ; i++)
            {
                var candidate = qualified + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_used.Add(candidate)) return candidate;
            }
        }

        /// <summary>
        ///     The schema keys a caller can actually reach, closed over what they reference.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         ⚠️ Reachability decides, not membership of the manifest. The manifest holds
        ///         entities on purpose (the Client SG builds its SDK from it), so turning every type in
        ///         it into a schema would publish <b>internal shapes</b>: measured, 7 orphans out of 85
        ///         schemas in one application, all seven entities, plus <c>Order</c> in conformance. A
        ///         generated client turns each into a class no call can produce or consume.
        ///     </para>
        ///     <para>
        ///         Reachability and not kind. Excluding entities would be the easy filter and the
        ///         wrong one: an endpoint that answers with an entity would be left with a <c>$ref</c> pointing
        ///         at nothing, which is a broken document rather than a noisy one.
        ///     </para>
        ///     <para>
        ///         The closure walks properties, because a DTO nobody returns directly is still
        ///         reachable when a returned one holds it. <c>ProblemDetails</c> and the request
        ///         schemas are roots: every error response names the first, and the operation names
        ///         the second.
        ///     </para>
        /// </remarks>
        public HashSet<string> Reachable(List<EndpointDto> endpoints)
        {
            var byKey = new Dictionary<string, TypeDto>(StringComparer.Ordinal);
            foreach (var entry in _entries)
                byKey[entry.Key] = entry.Value;

            var reached = new HashSet<string>(StringComparer.Ordinal) { ProblemDetailsSchema };
            var pending = new Stack<string>();

            void Root(string? key)
            {
                if (key is not null && reached.Add(key))
                    pending.Push(key);
            }

            // ⚠️ Unwrapped exactly as RenderPropertyType unwraps it. A property typed
            // List<OrderLineDto> resolves to nothing by name, so reaching only the outer type would
            // drop every element type from the closure — and dropping a schema something references
            // is a dangling $ref, which is worse than the orphan this method exists to remove.
            void RootFromType(string? clrType, int depth = 0)
            {
                if (depth > 8) return;

                var type = clrType?.Replace("global::", "").Trim().TrimEnd('?');
                if (string.IsNullOrEmpty(type)) return;

                if (TryUnwrap(type!, CollectionWrappers, out var element))
                {
                    RootFromType(element, depth + 1);
                    return;
                }

                if (TryUnwrapDictionaryValue(type!, out var value))
                {
                    RootFromType(value, depth + 1);
                    return;
                }

                if (type!.EndsWith("[]", StringComparison.Ordinal))
                {
                    RootFromType(type.Substring(0, type.Length - 2), depth + 1);
                    return;
                }

                Root(ResolveSchema(type));
            }

            foreach (var ep in endpoints)
            {
                if (ResolveResponse(ep.Response?.Type) is { } response)
                {
                    Root(response.Name);

                    // A paged envelope is a schema of its own that wraps the item: reaching the
                    // envelope has to reach what it holds, or the item is dropped from under it.
                    foreach (var envelope in _pagedEnvelopeOrder)
                        if (string.Equals(envelope.Key, response.Name, StringComparison.Ordinal))
                            Root(envelope.Value);
                }

                if (ep.RequestBody?.Properties is { Count: > 0 } bodyProps)
                    foreach (var p in bodyProps)
                        RootFromType(p.Type);
            }

            while (pending.Count > 0)
            {
                if (!byKey.TryGetValue(pending.Pop(), out var type) || type.Properties is null)
                    continue;

                foreach (var p in type.Properties)
                    RootFromType(p.Type);
            }

            return reached;
        }

        private static string SimpleNameOf(string fqn)
            => fqn.Contains('.') ? fqn.Substring(fqn.LastIndexOf('.') + 1) : fqn;

        private static string? Normalize(string? fqn)
            => fqn?.Replace("global::", "").Trim().TrimEnd('?');
    }

    private static void RenderProblemDetailsSchema(MetadataJsonBuilder b)
    {
        b.Property(ProblemDetailsSchema).StartObject();
        b.Property("type").Value("object");
        b.Property("properties").StartObject();
        b.Property("type").StartObject().Property("type").Value("string").EndObject();
        b.Property("title").StartObject().Property("type").Value("string").EndObject();
        b.Property("status").StartObject().Property("type").Value("integer").EndObject();
        b.Property("detail").StartObject().Property("type").Value("string").EndObject();
        b.Property("code").StartObject().Property("type").Value("string").Property("description").Value("Error code (e.g. NOT_FOUND, CONFLICT)").EndObject();
        b.EndObject();
        b.EndObject();
    }

    /// <summary>
    ///     Writes the type half of one property's schema: the members that say what the value is, with
    ///     the caller owning the surrounding object and anything else it wants to add.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both callers go through it rather than writing <c>"type"</c> from
    ///         <c>MapJsonSchemaType</c>, which maps seven primitives and closes with
    ///         <c>_ => "string"</c> — so a list, a dictionary, a nested object and an enum would all be
    ///         published as strings. Measured on the reference application: <b>43 properties</b> whose
    ///         declared type would be a lie, and 21 of the 22 types involved already have a schema in
    ///         the same document. The catalogue has the answer, and this asks it.
    ///     </para>
    ///     <para>
    ///         The order matters. A collection is unwrapped before anything else, because
    ///         <c>List&lt;Guest&gt;</c> is not a Guest; <c>byte[]</c> is not a collection, because JSON
    ///         carries it as a base64 string; and an enum the manifest does not describe falls back to
    ///         <c>string</c> rather than to nothing, because that is what
    ///         <c>JsonStringEnumConverter</c> puts on the wire.
    ///     </para>
    ///     <para>
    ///         The last resort writes <b>nothing</b> — an empty schema, which in JSON Schema means "any
    ///         value". It is the one honest thing to say about a type the manifest does not carry, and
    ///         it is strictly better than the string it replaces: an unconstrained schema tells a client
    ///         generator it does not know, while <c>"type": "string"</c> tells it something false.
    ///     </para>
    /// </remarks>
    private static void RenderPropertyType(
        MetadataJsonBuilder b, SchemaCatalog catalog, string? clrType, bool isEnum,
        bool isNullable = false, int depth = 0)
    {
        // A self-referencing generic cannot occur in a manifest type name, but the guard costs nothing
        // and a generator that loops is worse than one that says "any".
        if (depth > 8) return;

        var type = clrType?.Replace("global::", "").Trim().TrimEnd('?');
        if (string.IsNullOrEmpty(type)) return;

        var simple = SimpleName(type!);

        // Base64 on the wire, not an array of numbers.
        if (string.Equals(type, "byte[]", StringComparison.Ordinal)
            || string.Equals(simple, "Byte[]", StringComparison.Ordinal))
        {
            Type(b, "string", isNullable);
            b.Property("format").Value("byte");
            return;
        }

        if (TryUnwrap(type!, CollectionWrappers, out var element))
        {
            Type(b, "array", isNullable);
            b.Property("items").StartObject();
            RenderPropertyType(b, catalog, element, isEnum: false, isNullable: false, depth + 1);
            b.EndObject();
            return;
        }

        if (TryUnwrapDictionaryValue(type!, out var value))
        {
            Type(b, "object", isNullable);
            b.Property("additionalProperties").StartObject();
            RenderPropertyType(b, catalog, value, isEnum: false, isNullable: false, depth + 1);
            b.EndObject();
            return;
        }

        // A stream is bytes the endpoint writes, and the document should say so rather than "string".
        if (string.Equals(simple, "Stream", StringComparison.Ordinal))
        {
            Type(b, "string", isNullable);
            b.Property("format").Value("binary");
            return;
        }

        if (IsPrimitiveSchemaType(type))
        {
            Type(b, MapJsonSchemaType(type), isNullable);
            if (MapJsonSchemaFormat(type) is { } fmt) b.Property("format").Value(fmt);
            return;
        }

        if (catalog.ResolveSchema(type) is { } schema)
        {
            // A $ref cannot be made nullable by a sibling keyword — in 3.1 the union is spelled out.
            if (isNullable)
            {
                b.Property("anyOf").StartArray();
                b.StartObject().Property("$ref").Value($"#/components/schemas/{schema}").EndObject();
                b.StartObject().Property("type").Value("null").EndObject();
                b.EndArray();
            }
            else
            {
                b.Property("$ref").Value($"#/components/schemas/{schema}");
            }

            return;
        }

        // An enum from outside the manifest — System.DayOfWeek and its kind. The members are unknown,
        // but the shape is not.
        if (isEnum)
        {
            Type(b, "string", isNullable);
            return;
        }

        // Nothing true left to say. An empty schema says exactly that.
    }

    /// <summary>
    ///     Writes <c>type</c>, as a union with <c>null</c> when the property admits one.
    /// </summary>
    /// <remarks>
    ///     The document declares OpenAPI <b>3.1</b>, where <c>nullable</c> is not a keyword — it was
    ///     3.0's, and 3.1 moved to JSON Schema, which spells the same thing as a type union. Emitted as
    ///     <c>"nullable": true</c> it is an unknown member every reader drops, so <b>128 optional
    ///     properties in the reference application were published as if they could never be null</b>.
    ///     A generated client makes them non-optional and rejects a legal response.
    /// </remarks>
    private static void Type(MetadataJsonBuilder b, string type, bool isNullable)
    {
        if (!isNullable)
        {
            b.Property("type").Value(type);
            return;
        }

        b.Property("type").StartArray();
        b.Value(type);
        b.Value("null");
        b.EndArray();
    }

    /// <summary>Whether <c>MapJsonSchemaType</c> knows this type rather than falling back to string.</summary>
    private static bool IsPrimitiveSchemaType(string? clrType)
    {
        if (clrType is null) return false;
        var simple = SimpleName(clrType).ToLowerInvariant();
        return simple is "guid" or "string" or "int" or "int32" or "int64" or "long" or "short" or "byte"
            or "bool" or "boolean" or "decimal" or "double" or "float" or "single"
            or "dateonly" or "datetime" or "datetimeoffset" or "timeonly" or "timespan" or "char";
    }

    private static string SimpleName(string type)
    {
        var name = type;
        var open = name.IndexOf('<');
        if (open >= 0) name = name.Substring(0, open);
        name = name.Replace("System.", "");
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    /// <summary>The item type of <c>T[]</c> or of a wrapper in <paramref name="wrappers" />.</summary>
    private static bool TryUnwrap(string type, HashSet<string> wrappers, out string? inner)
    {
        inner = null;

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            inner = type.Substring(0, type.Length - 2);
            return true;
        }

        var open = type.IndexOf('<');
        var close = type.LastIndexOf('>');
        if (open < 0 || close <= open) return false;
        if (!wrappers.Contains(SimpleName(type))) return false;

        // Not a list pattern: it needs System.Index, which netstandard2.0 does not have (CS0518).
        var args = SplitTopLevel(type.Substring(open + 1, close - open - 1));
        if (args.Count != 1) return false;

        inner = args[0];
        return true;
    }

    /// <summary>The value type of a dictionary wrapper — the key is a JSON object key whatever it is.</summary>
    private static bool TryUnwrapDictionaryValue(string type, out string? value)
    {
        value = null;
        var open = type.IndexOf('<');
        var close = type.LastIndexOf('>');
        if (open < 0 || close <= open) return false;
        if (!DictionaryWrappers.Contains(SimpleName(type))) return false;

        var args = SplitTopLevel(type.Substring(open + 1, close - open - 1));
        if (args.Count != 2) return false;

        value = args[1];
        return true;
    }

    /// <summary>Splits generic arguments on commas that are not inside a nested argument list.</summary>
    private static List<string> SplitTopLevel(string args)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0:
                    parts.Add(args.Substring(start, i - start).Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(args.Substring(start).Trim());
        return parts;
    }

    private static void RenderTypeSchemas(
        MetadataJsonBuilder b, SchemaCatalog catalog, HashSet<string> reachable)
    {
        foreach (var entry in catalog.Entries)
        {
            // A type the manifest carries for another consumer — the Client SG reads entities from it —
            // is not part of this contract unless an operation can reach it.
            if (!reachable.Contains(entry.Key))
                continue;

            var t = entry.Value;
            b.Property(entry.Key).StartObject();

            // A localized string is a culture-to-value map on the wire — its converter writes
            // {"en": "…", "it": "…"} — while the type's own members are Value/UIValue/DataValue. A schema
            // built from the members describes an API nobody implements: a client generated from it
            // would send {"value": "…"} and the field would arrive empty.
            if (LocalizedStringType.Matches(t.Type) || LocalizedStringType.Matches(t.SimpleName))
            {
                b.Property("type").Value("object");
                b.Property("additionalProperties").StartObject();
                b.Property("type").Value("string");
                b.EndObject();
            }
            else if (t.Kind == "enum")
            {
                // The manifest carries enum MEMBER NAMES only ("values"), and every enum-typed property
                // below is mapped to "type": "string" — matching the JsonStringEnumConverter the host
                // registers. A numeric enum schema would need the member values, which the manifest does
                // not carry, so string is the only representation that is actually true of the payload.
                b.Property("type").Value("string");
                if (t.EnumValues is { Count: > 0 })
                {
                    b.Property("enum").StartArray();
                    foreach (var v in t.EnumValues) b.Value(v);
                    b.EndArray();
                }
            }
            else if (t.Properties is { Count: > 0 })
            {
                b.Property("type").Value("object");
                b.Property("properties").StartObject();
                var required = new List<string>();

                foreach (var p in t.Properties)
                {
                    if (p.Name is null) continue;
                    // Same rule as the request half: the wire name when there is one. A response
                    // published under the property's name is worse than a request published under it —
                    // a request under the wrong name is refused, a response under the wrong name is a
                    // null the client never reports.
                    var camelName = p.WireName ?? ToCamelCase(p.Name);

                    b.Property(camelName).StartObject();
                    RenderPropertyType(b, catalog, p.Type, p.IsEnum, p.IsNullable);
                    RenderConstraints(b, p);
                    b.EndObject();

                    if (p.IsRequired) required.Add(camelName);
                }
                b.EndObject(); // properties

                if (required.Count > 0)
                {
                    b.Property("required").StartArray();
                    foreach (var r in required) b.Value(r);
                    b.EndArray();
                }
            }
            b.EndObject();
        }
    }

    /// <summary>
    ///     The success envelope a paged <c>[Query]</c> endpoint answers with. It mirrors the serialised
    ///     shape of <c>PagedResult&lt;T&gt;</c> minus <c>error</c>: a failed query is turned into a 400
    ///     <c>application/problem+json</c> by the generated handler, so it never appears in a 2xx body.
    /// </summary>
    private static void RenderPagedEnvelopeSchemas(MetadataJsonBuilder b, SchemaCatalog catalog)
    {
        foreach (var envelope in catalog.PagedEnvelopes)
        {
            b.Property(envelope.Key).StartObject();
            b.Property("type").Value("object");
            b.Property("properties").StartObject();

            b.Property("items").StartObject();
            b.Property("type").Value("array");
            b.Property("items").StartObject();
            b.Property("$ref").Value($"#/components/schemas/{envelope.Value}");
            b.EndObject();
            b.EndObject();

            foreach (var scalar in new[] { "totalCount", "page", "pageSize", "totalPages", "skip" })
                b.Property(scalar).StartObject().Property("type").Value("integer").EndObject();
            foreach (var flag in new[] { "isSuccess", "isFailure", "hasPreviousPage", "hasNextPage" })
                b.Property(flag).StartObject().Property("type").Value("boolean").EndObject();

            b.EndObject(); // properties

            b.Property("required").StartArray();
            foreach (var r in new[] { "items", "totalCount", "page", "pageSize" }) b.Value(r);
            b.EndArray();

            b.EndObject();
        }
    }

    private static void RenderRequestSchemas(
        MetadataJsonBuilder b, List<EndpointDto> endpoints, SchemaCatalog catalog)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ep in endpoints)
        {
            if (ep.RequestBody?.Properties is not { Count: > 0 }) continue;
            // A directly-bound body whose type has its own schema needs no wrapper describing it.
            if (DirectBodySchemaKey(catalog, ep) is not null) continue;
            var name = RequestSchemaName(ep);
            if (!generated.Add(name)) continue;

            b.Property(name).StartObject();
            b.Property("type").Value("object");
            b.Property("properties").StartObject();
            var required = new List<string>();

            foreach (var p in ep.RequestBody.Properties)
            {
                if (p.Name is null) continue;
                // The wire name when there is one: the deserializer reads [JsonPropertyName] and a
                // schema that ignored it described a request the endpoint would refuse.
                var camelName = p.WireName ?? ToCamelCase(p.Name);
                b.Property(camelName).StartObject();
                RenderPropertyType(b, catalog, p.Type, p.IsEnum, p.IsNullable);
                RenderConstraints(b, p);
                b.EndObject();
                if (p.IsRequired) required.Add(camelName);
            }
            b.EndObject(); // properties

            if (required.Count > 0)
            {
                b.Property("required").StartArray();
                foreach (var r in required) b.Value(r);
                b.EndArray();
            }
            b.EndObject();
        }
    }
}
