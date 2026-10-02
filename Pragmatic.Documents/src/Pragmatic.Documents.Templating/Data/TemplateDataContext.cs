using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Holds named data sources for template resolution.
/// Each source is accessible by name: <c>{{customer.name}}</c> looks up source "customer", then property "name".
/// </summary>
public sealed class TemplateDataContext
{
    private readonly Dictionary<string, object?> _sources = [];
    private readonly Dictionary<string, Func<CancellationToken, ValueTask<object?>>> _asyncSources = [];
    // Thread-safe: concurrent template resolution can hit the same async source simultaneously.
    private readonly ConcurrentDictionary<string, object?> _resolvedAsyncCache = new();
    private readonly List<IPropertyAccessor> _accessors = [DictionaryPropertyAccessor.Instance];
    private readonly TemplateDataContext? _parent;

    /// <summary>Warnings emitted during resolution (unresolved paths, etc.).</summary>
    public IReadOnlyList<TemplateWarning> Warnings => _warnings;
    private readonly List<TemplateWarning> _warnings;

    public CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    /// <summary>Translation resolver for <c>t:</c> expressions. Set via I18N integration package.</summary>
    public Expressions.ITranslationResolver? TranslationResolver { get; private set; }

    public TemplateDataContext()
    {
        _warnings = [];
    }

    private TemplateDataContext(TemplateDataContext parent)
    {
        _parent = parent;
        _warnings = parent._warnings; // share warning list with parent
        Culture = parent.Culture;
        TranslationResolver = parent.TranslationResolver;
        _accessors = parent._accessors;
    }

    /// <summary>Add a named data source (any object — properties accessed via reflection or registered accessor).</summary>
    public TemplateDataContext AddSource(string name, object? value)
    {
        _sources[name] = value;
        return this;
    }

    /// <summary>Add a typed data source (type preserved for IntelliSense/SG).</summary>
    public TemplateDataContext AddSource<T>(string name, T value) where T : class
    {
        _sources[name] = value;
        return this;
    }

    /// <summary>Add an async data source (resolved on first access, cached).</summary>
    public TemplateDataContext AddSource(string name, Func<CancellationToken, ValueTask<object?>> factory)
    {
        _asyncSources[name] = factory;
        return this;
    }

    /// <summary>Create a TemplateDataContext from a <see cref="DataSourceCatalog"/>.</summary>
    public static TemplateDataContext FromCatalog(DataSourceCatalog catalog, string? culture = null)
        => catalog.ToDataContext(culture);

    public TemplateDataContext WithCulture(string cultureCode)
    {
        Culture = CultureInfo.GetCultureInfo(cultureCode);
        return this;
    }

    public TemplateDataContext WithCulture(CultureInfo culture)
    {
        Culture = culture;
        return this;
    }

    public TemplateDataContext WithTranslationResolver(Expressions.ITranslationResolver resolver)
    {
        TranslationResolver = resolver;
        return this;
    }

    /// <summary>
    ///     Records something the template asked for and did not get that the data context cannot see on
    ///     its own — a translation nobody wrote, say — on the same channel as a missing path.
    /// </summary>
    public TemplateDataContext AddWarning(TemplateWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        _warnings.Add(warning);
        return this;
    }

    public TemplateDataContext WithAccessor(IPropertyAccessor accessor)
    {
        _accessors.Insert(0, accessor); // Higher priority
        return this;
    }

    /// <summary>Create a child scope with an additional named value (for $for iteration).</summary>
    public TemplateDataContext CreateChildScope(string itemName, object? item)
    {
        var child = new TemplateDataContext(this);
        child._sources[itemName] = item;
        return child;
    }

    /// <summary>
    /// Resolve a dotted path: <c>customer.address.city</c>.
    /// First segment is the source name, rest is property path.
    /// </summary>
    public async ValueTask<object?> ResolveAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // Split into source name and property path
        var dotIndex = path.IndexOf('.');
        var sourceName = dotIndex >= 0 ? path[..dotIndex] : path;
        var propertyPath = dotIndex >= 0 ? path[(dotIndex + 1)..] : null;

        // Look up the source value
        var root = await GetSourceAsync(sourceName, ct);

        if (root is null)
        {
            if (_parent is null) // Only warn on root context, not child scopes searching parent
                _warnings.Add(new TemplateWarning(path, $"Source '{sourceName}' not found"));
            return null;
        }

        // If no property path, return root
        if (propertyPath is null) return root;

        // ⚠️ Whether the path could be FOLLOWED, not whether it arrived at something. A property that is
        // there and empty is a fact about the data; a property nobody provides is a fact about the
        // template, and only the second one is worth a warning. Warning on `result is null` made the two
        // indistinguishable, and an application cannot act on a channel that reports both.
        if (!TryNavigatePath(root, propertyPath, out var result))
            _warnings.Add(new TemplateWarning(path, $"Property '{propertyPath}' not found on source '{sourceName}'"));

        return result;
    }

    /// <summary>Resolve a collection from the data context.</summary>
    public async ValueTask<IEnumerable?> ResolveCollectionAsync(string path, CancellationToken ct = default)
    {
        var result = await ResolveAsync(path, ct);
        return result as IEnumerable;
    }

    private async ValueTask<object?> GetSourceAsync(string name, CancellationToken ct)
    {
        // Check local sources first
        if (_sources.TryGetValue(name, out var value)) return value;

        // Check async sources
        if (_asyncSources.TryGetValue(name, out var factory))
        {
            if (!_resolvedAsyncCache.TryGetValue(name, out var cached))
            {
                cached = await factory(ct);
                _resolvedAsyncCache[name] = cached;
            }
            return cached;
        }

        // Check parent
        if (_parent is not null) return await _parent.GetSourceAsync(name, ct);

        return null;
    }

    // Caps how far a single dotted path may walk the object graph — defence-in-depth against a template
    // navigating deep into the reachable public-property graph.
    private const int MaxPathSegments = 32;

    /// <summary>
    ///     Walks a dotted path, and says whether it could be walked at all.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when every segment named something that exists — the value may still
    ///     be null, and that is a legitimate answer. <see langword="false" /> when a segment named
    ///     nothing, or when the walk hit a null before its last segment and could not go on.
    /// </returns>
    private bool TryNavigatePath(object current, string path, out object? value)
    {
        var segments = path.Split('.');
        if (segments.Length > MaxPathSegments)
            throw new InvalidOperationException(
                $"Property path '{path}' exceeds the maximum depth of {MaxPathSegments} segments.");

        object? cursor = current;

        foreach (var segment in segments)
        {
            // The walk cannot continue through a null, and stopping early is not the same as arriving
            // at an empty value: `case.decision.signedBy` with no decision asked something the data
            // cannot answer.
            if (cursor is null)
            {
                value = null;

                return false;
            }

            if (!TryGetProperty(cursor, segment, out cursor))
            {
                value = null;

                return false;
            }
        }

        value = cursor;

        return true;
    }

    private bool TryGetProperty(object target, string propertyName, out object? value)
    {
        foreach (var accessor in _accessors)
        {
            if (accessor.HasProperty(target, propertyName))
            {
                value = accessor.GetValue(target, propertyName);

                return true;
            }
        }

        // The reflection fallback below answers both questions at once, so ask it whether the property
        // exists before asking for its value.
        if (RuntimeFeature.IsDynamicCodeSupported
            && ReflectionPropertyAccessor.Instance.HasProperty(target, propertyName))
        {
            value = ReflectionPropertyAccessor.Instance.GetValue(target, propertyName);

            return true;
        }

        value = GetProperty(target, propertyName);

        return false;
    }

    private object? GetProperty(object target, string propertyName)
    {
        foreach (var accessor in _accessors)
        {
            if (accessor.HasProperty(target, propertyName))
                return accessor.GetValue(target, propertyName);
        }

        // No registered accessor claims this property — nothing generates one; they come from
        // WithAccessor. On a JIT runtime, fall back to cached reflection: a template names properties in
        // text no compiler has seen, so refusing here would break a feature whose whole point is late
        // binding.
        //
        // Under Native AOT there is no such fallback. Reflecting over a trimmed type finds nothing and
        // silently renders an empty value, and a template that quietly drops a field is worse than one
        // that fails — this is the same silent-fallback shape that produced the 200-with-an-empty-body
        // and the query filter that "worked". Say which property, and which type, and stop.
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new InvalidOperationException(
                $"No accessor covers '{propertyName}' on '{target.GetType()}', and under Native AOT there "
                + "is no reflection fallback. Pass the value through a dictionary, or register an "
                + "IPropertyAccessor for the type with WithAccessor.");

        return ReflectionPropertyAccessor.Instance.GetValue(target, propertyName);
    }
}
