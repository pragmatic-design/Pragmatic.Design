using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator;

/// <summary>
///     Code generation methods: interface, HTTP client, DTOs, enums, errors, DI registration.
/// </summary>
public sealed partial class PragmaticClientGenerator
{
    private static void GenerateInterface(
        SourceProductionContext ctx, string ns, string boundary, List<EndpointDto> endpoints)
        => Emit(ctx, new Templates.ClientInterfaceTemplate(ns, boundary, endpoints));

    /// <summary>The client method name for an endpoint: the last segment of its operationId.</summary>
    internal static string MethodName(EndpointDto ep)
        => ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown";

    /// <summary>The Result type the endpoint's method returns.</summary>
    internal static string ReturnType(EndpointDto ep)
        => ep.IsVoid
            ? "Pragmatic.Result.VoidResult<Pragmatic.Result.IError>"
            : $"Pragmatic.Result.Result<{SimplifyType(ep.Response?.Type)}, Pragmatic.Result.IError>";

    private static void GenerateHttpClient(
        SourceProductionContext ctx, string ns, string boundary, List<EndpointDto> endpoints, List<ErrorInfo> errorTypes)
    {
        _wireTypes ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var ep in endpoints)
        {
            if (!ep.IsVoid && SimplifyType(ep.Response?.Type) is { Length: > 0 } response)
                _wireTypes.Add(response);

            if (HasJsonBody(ep))
                _wireTypes.Add((ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown") + "Request");
        }

        Emit(ctx, new Templates.ClientHttpClientTemplate(ns, boundary, endpoints, errorTypes));
    }

    /// <summary>Replaces <c>{name}</c> / <c>{name:constraint}</c> / <c>{name?}</c> in a route template.</summary>
    internal static string ReplaceRouteToken(string route, string name, string replacement)
    {
        var idx = 0;
        while (idx < route.Length)
        {
            var start = route.IndexOf('{', idx);
            if (start < 0) break;
            var end = route.IndexOf('}', start);
            if (end < 0) break;

            var token = route.Substring(start + 1, end - start - 1);
            if (string.Equals(StripConstraint(token), name, StringComparison.Ordinal))
                return route.Substring(0, start) + replacement + route.Substring(end + 1);

            idx = end + 1;
        }

        return route;
    }

    internal static string GetJsonReader(string jsonName, string? typeFqn)
    {
        var simple = SimplifyType(typeFqn);
        var prop = $"root.TryGetProperty(\"{jsonName}\", out var _{jsonName}) ? ";
        return simple switch
        {
            "string" => $"{prop}_{jsonName}.GetString() : null",
            "int" => $"{prop}_{jsonName}.GetInt32() : null",
            "long" => $"{prop}_{jsonName}.GetInt64() : null",
            "bool" => $"{prop}_{jsonName}.GetBoolean() : null",
            "decimal" => $"{prop}_{jsonName}.GetDecimal() : null",
            "double" => $"{prop}_{jsonName}.GetDouble() : null",
            "System.Guid" => $"{prop}_{jsonName}.GetGuid() : null",
            "System.DateTimeOffset" => $"{prop}System.DateTimeOffset.Parse(_{jsonName}.GetString()!) : null",
            _ => $"{prop}_{jsonName}.GetString() : null"
        };
    }

    private static void GenerateRequestDtos(
        SourceProductionContext ctx, string ns, List<EndpointDto> endpoints)
    {
        var generated = new HashSet<string>();
        foreach (var ep in endpoints)
        {
            // A GET's "body" properties travel in the query string, so no request DTO is needed for it.
            if (!HasJsonBody(ep)) continue;
            var dtoName = (ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown") + "Request";
            if (!generated.Add(dtoName)) continue;

            var properties = new List<Templates.ClientDtoProperty>();
            foreach (var p in ep.RequestBody?.Properties ?? [])
            {
                if (p.Name is null) continue;
                properties.Add(new Templates.ClientDtoProperty($"{SimplifyType(p.Type)}?", p.Name, false));
            }

            RecordDto(dtoName, properties);
            Emit(ctx, new Templates.ClientDtoTemplate(ns, dtoName, properties));
        }
    }

    private static void GenerateResponseDtos(
        SourceProductionContext ctx, string ns, List<TypeDto>? types)
    {
        if (types is null) return;

        var generated = new HashSet<string>(StringComparer.Ordinal);

        // Process DTOs first (more specific), then entities (fallback)
        var ordered = types
            .Where(t => (t.Kind == "entity" || t.Kind == "dto") && t.Properties is { Count: > 0 })
            .OrderBy(t => t.Kind == "dto" ? 0 : 1);

        foreach (var type in ordered)
        {
            // Don't double-suffix: GuestDto stays GuestDto, Guest becomes GuestDto
            var dtoName = type.SimpleName!.EndsWith("Dto", StringComparison.Ordinal)
                ? type.SimpleName
                : type.SimpleName + "Dto";

            // Skip duplicate names (e.g., Guest entity → GuestDto AND GuestDto dto → GuestDto)
            if (!generated.Add(dtoName)) continue;

            var properties = new List<Templates.ClientDtoProperty>();
            foreach (var p in type.Properties!)
            {
                if (p.Name is null) continue;
                properties.Add(new Templates.ClientDtoProperty(
                    MapPropertyType(p.Type, p.IsNullable), p.Name, !p.IsNullable));
            }

            RecordDto(dtoName, properties);
            Emit(ctx, new Templates.ClientDtoTemplate(ns, dtoName, properties, $"DTO for {type.SimpleName}."));
        }
    }

    private static void GeneratePagedResult(SourceProductionContext ctx, string ns)
        => Emit(ctx, new Templates.ClientPagedResultTemplate(ns));

    /// <summary>Remembers a DTO's shape for the JSON context.</summary>
    private static void RecordDto(string name, List<Templates.ClientDtoProperty> properties)
    {
        _generatedDtos ??= [];
        if (!_generatedDtos.Any(d => string.Equals(d.Name, name, StringComparison.Ordinal)))
            _generatedDtos.Add(new ClientJsonContextBuilder.Dto(name, properties));
    }

    /// <summary>
    ///     Remembers the item type of a paged response. PagedResult&lt;T&gt; is one generic class, so the
    ///     context needs an entry per closed instantiation the endpoints return.
    /// </summary>
    private static void RecordPagedItem(string itemType)
    {
        _pagedItemTypes ??= new HashSet<string>(StringComparer.Ordinal);
        _pagedItemTypes.Add(itemType);
    }

    private static void GenerateEnums(
        SourceProductionContext ctx, string ns, List<TypeDto>? types)
    {
        if (types is null) return;

        foreach (var type in types.Where(t => t.Kind == "enum" && t.EnumValues is { Count: > 0 }))
        {
            // Only emit members that are usable as C# identifiers; anything else would make the whole
            // generated file uncompilable, taking every other type in it down with it.
            var members = type.EnumValues!.Where(IsValidIdentifier).ToList();
            if (members.Count == 0)
                continue;

            Emit(ctx, new Templates.ClientEnumTemplate(ns, type.SimpleName!, members));
        }
    }

    internal static string MapPropertyType(string? type, bool isNullable)
    {
        var baseType = SimplifyType(type);
        return isNullable ? $"{baseType}?" : baseType;
    }

    private static void GenerateErrors(
        SourceProductionContext ctx, string ns, List<TypeDto>? types)
    {
        if (types is null) return;

        foreach (var type in types.Where(t => t.Kind == "error"))
        {
            var extensions = new List<Templates.ClientDtoProperty>();
            foreach (var ext in type.Extensions ?? [])
            {
                if (ext.Name is null) continue;
                var propName = char.ToUpperInvariant(ext.Name[0]) + ext.Name.Substring(1);
                extensions.Add(new Templates.ClientDtoProperty(SimplifyType(ext.Type), propName, false));
            }

            Emit(ctx, new Templates.ClientErrorTemplate(
                ns, type.SimpleName!, EscapeLiteral(type.ErrorCode ?? "UNKNOWN"),
                type.ErrorStatusCode ?? 500, extensions));
        }
    }

    /// <summary>Adds a template's artifact to the compilation, skipping it when the template did not validate.</summary>
    private static void Emit(SourceProductionContext ctx, CSharpTemplate template)
    {
        var artifact = template.RenderOutput();
        if (artifact.TryGetSource(out var source))
            AddSourceOnce(ctx, artifact.HintName, source.ToString());
    }

    /// <summary>
    ///     Adds a source file, tolerating the same hint being produced twice.
    ///     <para>
    ///         Every manifest is generated into one shared <see cref="SourceProductionContext" />, and a type
    ///         reachable from several boundaries (LocalizedString, SortDirection, a shared entity) appears in
    ///         each of their manifests. Emitting it per manifest made <c>AddSource</c> throw on the duplicate
    ///         hint, and that exception took down the generation of an ENTIRE client — the boundary lost all
    ///         of its DTOs and interfaces, surfacing as dozens of CS0246 at the call site. PagedResult&lt;T&gt;
    ///         was already special-cased for the same reason; this handles the general case.
    ///     </para>
    ///     <para>
    ///         Identical shapes are deduplicated silently. Divergent ones keep the first and report
    ///         PRAG2304 rather than picking a winner in silence.
    ///     </para>
    /// </summary>
    private static void AddSourceOnce(SourceProductionContext ctx, string hintName, string source)
    {
        _emittedSources ??= new Dictionary<string, string>(StringComparer.Ordinal);

        if (!_emittedSources.TryGetValue(hintName, out var existing))
        {
            _emittedSources[hintName] = source;
            return;
        }

        if (string.Equals(existing, source, StringComparison.Ordinal))
            return;

        // The same type described by two manifests need not be a conflict: the assembly that DECLARES an
        // entity sees only its hand-written members, because the generated ones (Id, audit, soft-delete,
        // navigations) do not exist yet in that compilation — while an assembly that REFERENCES it reads
        // them from the compiled metadata. One description is then a subset of the other, and the complete
        // one is the truth about the type. Keep that one, so the output does not depend on which manifest
        // happened to be read first.
        if (Contains(source, existing))
        {
            _emittedSources[hintName] = source;
            return;
        }

        if (Contains(existing, source))
            return;

        // Neither contains the other: the same simple name really does denote two different shapes.
        ctx.ReportDiagnostic(Diagnostic.Create(
            ClientDiagnostics.SharedTypeShapeConflict, Location.None, hintName));
    }

    /// <summary>Whether every non-blank line of <paramref name="inner" /> also appears in <paramref name="outer" />.</summary>
    private static bool Contains(string outer, string inner)
    {
        var outerLines = new HashSet<string>(
            outer.Split('\n').Select(static l => l.Trim()), StringComparer.Ordinal);

        return inner.Split('\n')
            .Select(static l => l.Trim())
            .Where(static l => l.Length > 0)
            .All(outerLines.Contains);
    }

    /// <summary>
    ///     Writes the deduplicated sources into the compilation. Emission is deferred to the end of the run
    ///     because the best description of a shared type is only known once every manifest has been seen.
    /// </summary>
    internal static void FlushSources(SourceProductionContext ctx)
    {
        if (_emittedSources is null)
            return;

        foreach (var pair in _emittedSources)
            ctx.AddSource(pair.Key, pair.Value);
    }

    // Hint name → emitted text, for the lifetime of one generator run. Reset in Initialize's callback.
    [ThreadStatic] internal static Dictionary<string, string>? _emittedSources;

    /// <summary>
    ///     The DTOs this run generated, so one JsonSerializerContext can cover them all. Per-run, like
    ///     <see cref="_emittedSources" />.
    /// </summary>
    [ThreadStatic] internal static List<ClientJsonContextBuilder.Dto>? _generatedDtos;

    /// <summary>The item types of the paged responses, for the closed PagedResult&lt;T&gt; entries.</summary>
    [ThreadStatic] internal static HashSet<string>? _pagedItemTypes;

    /// <summary>Every type the endpoints send or read, which is what the context must cover.</summary>
    [ThreadStatic] internal static HashSet<string>? _wireTypes;

    private static void GenerateRegistration(
        SourceProductionContext ctx, string ns, string boundary)
    {
        var artifact = new Templates.ClientRegistrationTemplate(ns, boundary).RenderOutput();
        if (artifact.TryGetSource(out var source))
            AddSourceOnce(ctx, artifact.HintName, source.ToString());
    }

    // Helpers

    // Known entity types from manifest — populated per-manifest in GenerateFromManifest
    [ThreadStatic] private static HashSet<string>? _knownEntityTypes;

    // Known enum types from manifest — they are emitted by GenerateEnums, so they can be named directly
    // instead of collapsing to object (which would make a typed query parameter useless).
    [ThreadStatic] internal static HashSet<string>? _knownEnumTypes;

    // Type names the manifest does not describe; reported once per manifest as PRAG2301.
    [ThreadStatic] internal static HashSet<string>? _unresolvedTypes;

    /// <summary>
    ///     The response types this generator emits as value types. Everything else it can produce — object,
    ///     string, an array, PagedResult&lt;T&gt;, a generated DTO — is a reference type.
    ///     <para>
    ///         The distinction decides whether a <c>payload is null</c> check can be emitted at all: on a value
    ///         type the compiler reports it as always false (CS8073), which a consumer building with
    ///         warnings-as-errors would fail on.
    ///     </para>
    /// </summary>
    private static readonly HashSet<string> ValueResponseTypes = new(StringComparer.Ordinal)
    {
        "System.Guid", "int", "long", "bool", "decimal", "double",
        "System.DateOnly", "System.TimeOnly", "System.DateTime", "System.DateTimeOffset",
        "System.TimeSpan", "System.Byte", "System.Int16", "System.Single", "System.Uri"
    };

    internal static bool IsReferenceResponseType(string responseType)
    {
        // System.Uri is a reference type despite living in the BCL list above; treat it as one.
        if (responseType == "System.Uri")
            return true;
        if (ValueResponseTypes.Contains(responseType))
            return false;
        // Enums from the manifest are value types too.
        return _knownEnumTypes is null || !_knownEnumTypes.Contains(responseType);
    }

    internal static string TrackUnresolved(string simpleName)
    {
        if (simpleName.Length > 0)
            (_unresolvedTypes ??= new HashSet<string>(StringComparer.Ordinal)).Add(simpleName);
        return "object";
    }

    private static string DeriveBoundaryName(string assembly)
    {
        var parts = assembly.Split('.');
        return parts.Length >= 2 ? parts[parts.Length - 1] : parts[0];
    }

    /// <summary>A value carried in the query string: an explicit query parameter, or a GET's "body" property.</summary>
    internal sealed class QueryValue(string name, string type)
    {
        public string Name { get; } = name;
        public string Type { get; } = type;
    }

    internal static bool IsGet(EndpointDto ep)
        => string.Equals(ep.HttpMethod ?? "GET", "GET", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     A request body is sent as a JSON payload only for verbs that carry one. For a GET the manifest still
    ///     reports a "requestBody" (the query object's properties), but on the wire those bind from the query
    ///     string — sending them as a body would force the verb to POST and the server would answer 405.
    /// </summary>
    internal static bool HasJsonBody(EndpointDto ep)
        => ep.RequestBody?.Properties is { Count: > 0 } && !IsGet(ep);

    /// <summary>Query-string values: explicit <c>in: "query"</c> parameters, plus a GET's body properties.</summary>
    internal static List<QueryValue> CollectQueryValues(EndpointDto ep)
    {
        var values = new List<QueryValue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ep.Parameters is not null)
            foreach (var p in ep.Parameters.Where(p => p.In == "query" && p.Name is not null))
                if (seen.Add(p.Name!))
                    values.Add(new QueryValue(p.Name!, SimplifyType(p.Type)));

        if (IsGet(ep) && ep.RequestBody?.Properties is { Count: > 0 } properties)
            foreach (var p in properties.Where(p => p.Name is not null))
                if (seen.Add(p.Name!))
                    values.Add(new QueryValue(p.Name!, SimplifyType(p.Type)));

        return values;
    }

    /// <summary>Path parameters, in route order: the ones declared in the manifest plus any only present in the route template.</summary>
    internal static List<QueryValue> CollectPathParams(EndpointDto ep)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ep.Parameters is not null)
            foreach (var p in ep.Parameters.Where(p => p.In == "path" && p.Name is not null))
            {
                var name = StripConstraint(p.Name!);
                declared[name] = SimplifyType(p.Type);
            }

        // Route order is what the URL needs, so walk the template and take the declared type when known.
        var ordered = new List<QueryValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in EnumerateRouteParams(ep.FullRoute))
        {
            if (!seen.Add(name)) continue;
            var type = declared.TryGetValue(name, out var declaredType)
                ? declaredType
                // Not in the manifest: infer Guid for *Id, string otherwise.
                : name.EndsWith("Id", StringComparison.Ordinal) ? "System.Guid" : "string";
            ordered.Add(new QueryValue(name, type));
        }

        // Declared but absent from the route (defensive): keep them so the signature stays complete.
        foreach (var kvp in declared)
            if (seen.Add(kvp.Key))
                ordered.Add(new QueryValue(kvp.Key, kvp.Value));

        return ordered;
    }

    internal static IEnumerable<string> EnumerateRouteParams(string? route)
    {
        if (route is null) yield break;
        var idx = 0;
        while (idx < route.Length)
        {
            var start = route.IndexOf('{', idx);
            if (start < 0) yield break;
            var end = route.IndexOf('}', start);
            if (end < 0) yield break;
            yield return StripConstraint(route.Substring(start + 1, end - start - 1));
            idx = end + 1;
        }
    }

    /// <summary>Strips a route constraint and optional marker: <c>id:guid</c> / <c>id?</c> → <c>id</c>.</summary>
    internal static string StripConstraint(string raw)
    {
        var colon = raw.IndexOf(':');
        var name = colon >= 0 ? raw.Substring(0, colon) : raw;
        return name.TrimEnd('?');
    }

    internal static string BuildParams(EndpointDto ep)
    {
        var parts = new List<string>();

        foreach (var p in CollectPathParams(ep))
            parts.Add($"{p.Type} {p.Name}");

        if (HasJsonBody(ep))
        {
            var reqName = (ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown") + "Request";
            parts.Add($"{reqName} request");
        }

        // Query values are optional (the manifest does not model requiredness reliably) so they must come last,
        // right before the trailing CancellationToken.
        foreach (var q in CollectQueryValues(ep))
            parts.Add($"{Nullable(q.Type)} {CamelCase(q.Name)} = null");

        return parts.Count > 0 ? string.Join(", ", parts) + ", " : "";
    }

    /// <summary>Makes a type nullable for an optional query parameter (reference types are already nullable).</summary>
    internal static string Nullable(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

    internal static string CamelCase(string name)
        => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    internal static string MethodForVerb(string verb) => verb switch
    {
        "POST" => "PostAsJsonAsync",
        "PUT" => "PutAsJsonAsync",
        "PATCH" => "PatchAsJsonAsync",
        _ => "PostAsJsonAsync"
    };

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        "Guid", "String", "string", "Int32", "int", "Int64", "long",
        "Boolean", "bool", "Decimal", "decimal", "Double", "double",
        "DateOnly", "TimeOnly", "DateTime", "DateTimeOffset", "Byte",
        // BCL types that reached the client as 'object' only because they were missing from this list.
        "Uri", "TimeSpan", "Single", "float", "Int16", "short",
        // BCL enum: the manifest deliberately omits System.* types, so the client has to know it.
        "DayOfWeek"
    };

    private static readonly HashSet<string> ListLikeGenerics = new(StringComparer.Ordinal)
    {
        "List", "IList", "IReadOnlyList", "ICollection", "IReadOnlyCollection", "IEnumerable"
    };

    // Resolves known generic shapes through the manifest type table.
    // Collection wrappers (List<T>, IReadOnlyList<T>, …) → T[]; PagedResult<T> → the generated
    // PagedResult<T> DTO (emitted once per client); everything else → object.
    internal static string SimplifyGeneric(string fqn)
    {
        var open = fqn.IndexOf('<');
        var close = fqn.LastIndexOf('>');
        if (open < 0 || close < open) return "object";

        var outerRaw = fqn.Substring(0, open).Replace("global::", "").Replace("System.", "");
        var outer = outerRaw.Contains('.') ? outerRaw.Substring(outerRaw.LastIndexOf('.') + 1) : outerRaw;
        var inner = fqn.Substring(open + 1, close - open - 1);

        // Only single-arg generics are resolved; multi-arg (e.g. Dictionary<,>) stays object.
        if (inner.Contains(',')) return "object";

        if (outer == "PagedResult")
        {
            var item = SimplifyType(inner);
            RecordPagedItem(item);
            return $"PagedResult<{item}>";
        }

        if (!ListLikeGenerics.Contains(outer)) return "object";

        return $"{SimplifyType(inner)}[]";
    }

    internal static string SimplifyType(string? fqn)
    {
        if (fqn is null) return "object";

        // An array is resolved through its element type. Without this the whole name — "GuestDto[]" — was
        // looked up in the manifest type table, never matched, and a perfectly well-described DTO came out
        // as object: a typed endpoint returning T[] lost its type for no reason.
        if (fqn.EndsWith("[]", StringComparison.Ordinal))
            return SimplifyType(fqn.Substring(0, fqn.Length - 2)) + "[]";

        // Resolve known collection wrappers through their element type instead of collapsing to object.
        // List<T>/IReadOnlyList<T>/IList<T>/ICollection<T>/IEnumerable<T> → T[]. Other generics → object.
        if (fqn.Contains('<') || fqn.Contains('>'))
            return SimplifyGeneric(fqn);
        var s = fqn.Replace("global::", "").Replace("System.", "");
        var simple = s.Contains('.') ? s.Substring(s.LastIndexOf('.') + 1) : s;
        // The manifest spells a nullable value type as "Foo?" — match on the underlying name and let the
        // caller re-apply nullability, otherwise every nullable enum/struct would fall through to object.
        simple = simple.TrimEnd('?');
        // Only emit known CLR types — domain types become object (until manifest includes full DTO schemas)
        return simple switch
        {
            "Guid" => "System.Guid",
            "String" or "string" => "string",
            "Int32" or "int" => "int",
            "Int64" or "long" => "long",
            "Boolean" or "bool" => "bool",
            "Decimal" or "decimal" => "decimal",
            "Double" or "double" => "double",
            "DateOnly" => "System.DateOnly",
            "DateTimeOffset" => "System.DateTimeOffset",
            // BCL types the manifest deliberately does not describe (IsWellKnownType filters System.*),
            // and which do not live directly under System — so the "System.{name}" fallback below would
            // produce a type that does not exist. A file payload is a Stream, not an object.
            "Stream" => "System.IO.Stream",
            _ when _knownEnumTypes is not null && _knownEnumTypes.Contains(simple) => simple,
            _ when _knownEntityTypes is not null && _knownEntityTypes.Contains(simple)
                => simple.EndsWith("Dto", StringComparison.Ordinal) ? simple : $"{simple}Dto",
            _ when KnownTypes.Contains(simple) => $"System.{simple}",
            // Nothing in the manifest describes this type, so the client can only expose it as object.
            // Recorded here and reported once per manifest as PRAG2301 — silently degrading a "typed client"
            // to object is exactly the kind of thing that should not be discovered at the call site.
            _ => TrackUnresolved(simple)
        };
    }

    /// <summary>
    ///     Escapes a manifest string for use inside a single-line XML doc comment. Newlines are collapsed:
    ///     a multi-line summary would otherwise break out of the <c>///</c> comment and emit uncompilable code.
    /// </summary>
    internal static string Escape(string s)
        => s.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");

    /// <summary>
    ///     Escapes a manifest string for use inside a C# string literal. A value carrying a quote or a
    ///     backslash would otherwise terminate the literal and produce uncompilable code.
    /// </summary>
    internal static string EscapeLiteral(string s)
        => s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

    /// <summary>
    ///     True when <paramref name="name"/> can be emitted as a C# identifier. Manifest-supplied enum member
    ///     names are used verbatim in generated code, so an unusable one must be skipped rather than emitted.
    /// </summary>
    internal static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name![0]) && name[0] != '_') return false;

        for (var i = 1; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;

        return true;
    }
}
