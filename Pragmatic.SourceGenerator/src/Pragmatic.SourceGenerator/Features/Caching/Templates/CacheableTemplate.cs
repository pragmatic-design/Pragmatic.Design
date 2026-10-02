using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Caching.Models;

namespace Pragmatic.SourceGenerator.Features.Caching.Templates;

internal sealed class CacheableTemplate : CSharpTemplate
{
    private readonly CacheableModel _model;

    public CacheableTemplate(CacheableModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Caching";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Cacheable] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Cache", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsings("System.Collections.Immutable", "System.Runtime.CompilerServices");

        AppendNamespace(_model.Namespace);
        AppendLine();

        RenderCacheablePartialType();

        AppendLine();

        RenderCacheKeysHelperClass();
    }

    private void RenderCacheablePartialType()
    {
        XmlSummary($"Generated ICacheable implementation for {_model.TypeName}.");

        var accessibility = TemplateHelpers.ParseAccessibility(_model.Accessibility);
        var mods = new ClassModifiers { Partial = true };

        switch (_model.TypeKind)
        {
            case "record":
                Record(_model.TypeName, RenderCacheableMethods, null, accessModifier: accessibility, modifiers: mods);
                break;
            case "record struct":
                RecordStruct(_model.TypeName, RenderCacheableMethods, null, accessModifier: accessibility, modifiers: mods);
                break;
            case "struct":
                Struct(_model.TypeName, RenderCacheableMethods, ["global::Pragmatic.Caching.ICacheable"], accessibility, mods);
                break;
            default:
                Class(_model.TypeName, RenderCacheableMethods, null, ["global::Pragmatic.Caching.ICacheable"], accessibility, mods);
                break;
        }
    }

    private void RenderCacheableMethods()
    {
        RenderGetCacheKey();
        AppendLine();
        RenderGetCacheOptions();

        // Generate CacheCategory property when a category is specified
        if (_model.CategoryTypeFqn is not null)
        {
            AppendLine();
            ExpressionProperty("CacheCategory", "global::System.Type?",
                $"typeof({_model.CategoryTypeFqn})");
        }
    }

    private void RenderGetCacheKey()
    {
        ExpressionMethod(
            "GetCacheKey",
            BuildCacheKeyExpression(),
            "string",
            null,
            modifiers: default,
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private void RenderGetCacheOptions()
    {
        var duration = ParseDuration(_model.Duration);
        var optionsInit = BuildOptionsInitializer(duration);

        // If all cache options are compile-time constants (no placeholder tags),
        // generate a static readonly field to avoid per-call allocation
        var allTagsAreConstant = _model.Tags.IsDefaultOrEmpty || _model.Tags.All(t => !t.Contains("{"));
        if (allTagsAreConstant)
        {
            AppendLine($"private static readonly global::Pragmatic.Caching.CacheEntryOptions __cacheOptions = new() {{ {optionsInit} }};");
            AppendLine();
            ExpressionMethod("GetCacheOptions", "__cacheOptions",
                "global::Pragmatic.Caching.CacheEntryOptions",
                null, modifiers: default,
                attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
        }
        else
        {
            // Dynamic tags (with placeholders) — must create per call
            Method("GetCacheOptions",
                () => { Return($"new global::Pragmatic.Caching.CacheEntryOptions {{ {optionsInit} }}"); },
                "global::Pragmatic.Caching.CacheEntryOptions");
        }
    }

    private void RenderCacheKeysHelperClass()
    {
        XmlSummary($"Static cache key helper for {_model.TypeName}.");

        var mods = new ClassModifiers { Partial = true, IsStatic = true };
        Class($"{_model.TypeName}CacheKeys", RenderCacheKeysMembers, null, null, AccessModifier.Public, mods);
    }

    private void RenderCacheKeysMembers()
    {
        var orderedProps = _model.KeyProperties.OrderBy(p => p.Order).ToList();

        if (orderedProps.Count == 0)
        {
            XmlSummary("Gets the cache key prefix for this type.");
            ExpressionProperty("Prefix", "string", $"\"{_model.FullTypeName}\"", isStatic: true);
        }
        else
        {
            var parameters = orderedProps.Select(p => new MethodParameter(p.Type, p.Name)).ToList();
            XmlSummary($"Creates a cache key for {_model.TypeName}.");
            ExpressionMethod(
                "Create",
                BuildStaticCacheKeyExpression(orderedProps),
                "string",
                parameters,
                modifiers: new MethodModifiers { IsStatic = true },
                attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
        }
    }

    // =========================================================================
    // Expression Builders
    // =========================================================================

    private string BuildCacheKeyExpression()
    {
        if (_model.KeyProperties.IsDefaultOrEmpty)
            return $"\"{_model.FullTypeName}\"";

        var orderedProps = _model.KeyProperties.OrderBy(p => p.Order).ToList();
        return $"$\"{_model.FullTypeName}:{string.Join(":", Fragments(orderedProps, p => p.Name))}\"";
    }

    private string BuildStaticCacheKeyExpression(List<CacheKeyPropertyModel> orderedProps)
        => $"$\"{_model.FullTypeName}:{string.Join(":", Fragments(orderedProps, p => TemplateHelpers.ToCamelCase(p.Name)))}\"";

    /// <summary>
    ///     One fragment per part, read from the root this caller supplies.
    /// </summary>
    /// <remarks>
    ///     The instance method reads the property (<c>Location</c>) and the static helper its parameter
    ///     (<c>location</c>); everything after the root is the same tail from the same model, so the two
    ///     keys cannot drift apart. ⚠️ A property with no parts contributes nothing — a complex object
    ///     the walk could not read, reported as PRAG1705 rather than keyed by its type name.
    /// </remarks>
    private static IEnumerable<string> Fragments(
        List<CacheKeyPropertyModel> orderedProps,
        Func<CacheKeyPropertyModel, string> root)
        => orderedProps.SelectMany(p => p.Parts.Select(part =>
            $"{EscapeLabel(part.Label)}={EscapeKeyPart(root(p) + part.Tail, part.IsCollection)}"));

    // The [CacheKey(Name = "...")] label is emitted verbatim into the interpolated key literal. Normal
    // labels are identifiers, but an author-supplied Name containing '"', '\' or '{'/'}' would break
    // compilation or forge an interpolation hole — escape those for safety (no-op for identifiers).
    private static string EscapeLabel(string label)
        => label.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("{", "{{").Replace("}", "}}");

    // Emits an interpolation hole that URI-escapes the value so a value containing ':' or '=' cannot
    // collide with an adjacent key segment (cross-record cache poisoning). Convert.ToString keeps it
    // type-safe (value types box, null → empty) without a '?.' that would not compile on value types.
    // Collections are serialized element-by-element: Convert.ToString on a List/array yields the TYPE
    // NAME (identical for any instance), which would collapse different lists onto one key and serve
    // stale data — so each element is escaped and joined with an (unescaped) ',' that escaped elements
    // can never contain.
    // Note: the 'global::' alias qualifier is NOT valid inside an interpolated-string hole, so the BCL
    // types are referenced as plain 'System.*' (always resolvable in the generated namespace).
    private static string EscapeKeyPart(string valueExpression, bool isCollection)
    {
        if (!isCollection)
            return $"{{System.Uri.EscapeDataString(System.Convert.ToString({valueExpression}, " +
                   $"System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}}";

        return $"{{({valueExpression} == null ? string.Empty : string.Join(\",\", " +
               $"System.Linq.Enumerable.Select({valueExpression}, __ck => System.Uri.EscapeDataString(" +
               $"System.Convert.ToString(__ck, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))))}}";
    }

    private string BuildOptionsInitializer(TimeSpan duration)
    {
        var parts = new List<string>();

        parts.Add(_model.Sliding
            ? $"SlidingDuration = global::System.TimeSpan.FromSeconds({duration.TotalSeconds})"
            : $"Duration = global::System.TimeSpan.FromSeconds({duration.TotalSeconds})");

        if (_model.Priority != 0)
            parts.Add($"Priority = (global::Pragmatic.Caching.CachePriority){_model.Priority}");

        if (!_model.Tags.IsDefaultOrEmpty)
        {
            var tagExpressions = _model.Tags.Select(ExpandTagPlaceholder);
            parts.Add($"Tags = [{string.Join(", ", tagExpressions)}]");
        }

        return string.Join(", ", parts);
    }

    private string ExpandTagPlaceholder(string tag)
    {
        if (!tag.Contains("{"))
            return $"\"{StringHelper.CSharpLiteral(tag)}\"";

        // Replace {PropName} with {this.PropName} so the generated interpolated string references the
        // instance property value. Expand over ALL properties (not just [CacheKey] ones) — a tag placeholder
        // may reference any property; iterating only KeyProperties left non-key placeholders un-expanded,
        // emitting a reference to an undeclared local (mirrors InvalidatorTemplate.ExpandPlaceholder).
        var result = tag;
        foreach (var propName in _model.AllPropertyNames)
            result = result.Replace($"{{{propName}}}", $"{{this.{propName}}}");

        // Escape backslash/quote in the literal portions so an author-supplied " or \ in a tag cannot
        // break out of the interpolated string. The {…} holes (now {this.Prop}) carry no " or \, so this
        // is safe. We do NOT escape braces — they are the intended interpolation mechanism here.
        result = result.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return $"$\"{result}\"";
    }

    private static TimeSpan ParseDuration(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return TimeSpan.FromMinutes(5);

        var match = CachingFeature.DurationRegex.Match(duration);
        if (match.Success)
        {
            var value = int.Parse(match.Groups[1].Value);
            return match.Groups[2].Value.ToLower() switch
            {
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                _ => TimeSpan.FromMinutes(5)
            };
        }

        return TimeSpan.TryParse(duration, out var ts) ? ts : TimeSpan.FromMinutes(5);
    }

}
