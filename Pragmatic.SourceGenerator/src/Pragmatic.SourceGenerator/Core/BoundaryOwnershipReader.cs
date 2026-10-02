using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Which boundaries this assembly declares, and which entities each one claims with
///     <c>[Owns&lt;TEntity&gt;]</c>.
/// </summary>
/// <remarks>
///     The one place that answers "what boundaries are in play here", for the entity side. The actions
///     side asks the same question through its own syntax pipeline, and both derive the default boundary
///     from <see cref="DefaultBoundaryTransform" /> so the name cannot drift between them.
/// </remarks>
internal static class BoundaryOwnershipReader
{
    private const string BoundaryAttributeName = "Pragmatic.Actions.Attributes.BoundaryAttribute";
    private const string ModuleAttributeName = "Pragmatic.Composition.Attributes.ModuleAttribute";
    private const string OwnsAttributePrefix = "Pragmatic.Actions.Attributes.OwnsAttribute";
    private const string BelongsToNamespace = "Pragmatic.Persistence.Entity";

    public static BoundaryOwnership Read(Compilation compilation, CancellationToken ct)
    {
        var boundaryAttr = compilation.GetTypeByMetadataName(BoundaryAttributeName);
        var declared = ImmutableArray.CreateBuilder<DeclaredBoundary>();
        INamedTypeSymbol? moduleType = null;
        var moduleCount = 0;

        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace, ct))
        {
            var isBoundary = false;
            foreach (var attr in type.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass is null)
                    continue;

                if (boundaryAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, boundaryAttr))
                    isBoundary = true;

                if (attrClass.OriginalDefinition.ToDisplayString() == ModuleAttributeName)
                {
                    moduleCount++;
                    moduleType ??= type;
                }
            }

            if (isBoundary)
                declared.Add(new DeclaredBoundary(
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    StripBoundarySuffix(type.Name),
                    CollectOwned(type),
                    LocationInfo.From(type.Locations.FirstOrDefault())));
        }

        if (declared.Count > 0)
            return new BoundaryOwnership(declared.ToImmutable(), moduleCount, IsDerived: false);

        // No [Boundary]: the module's own, the same one the actions pipeline synthesises.
        if (moduleType is null || CompositionDetector.IsHostProject(compilation))
            return new BoundaryOwnership(ImmutableArray<DeclaredBoundary>.Empty, moduleCount, IsDerived: false);

        var derived = DefaultBoundaryTransform.FromModule(moduleType);
        if (derived is null)
            return new BoundaryOwnership(ImmutableArray<DeclaredBoundary>.Empty, moduleCount, IsDerived: false);

        return new BoundaryOwnership(
            ImmutableArray.Create(new DeclaredBoundary(
                derived.FullTypeName,
                StripBoundarySuffix(derived.TypeName),
                ImmutableArray<string>.Empty,
                null)),
            moduleCount,
            IsDerived: true);
    }

    /// <summary>
    ///     The boundary an entity belongs to: its declared <c>[BelongsTo]</c>, else the boundary that
    ///     claims it with <c>[Owns&lt;T&gt;]</c>, else the single <c>[Boundary]</c> of its assembly.
    ///     Returned as a plain display string and a short name.
    /// </summary>
    /// <remarks>
    ///     One rule, five callers. Private copies of the same loop reading only the attribute would be
    ///     indistinguishable from correct while every entity carried one — and wrong in five places
    ///     the day they stopped. ⚠️ The <c>[Owns]</c> step is what makes an assembly with two
    ///     boundaries work: without it every entity there answers "no boundary", so a relation between
    ///     them is not a crossing and gets a navigation, an EF configuration and a constraint into the
    ///     other boundary's table. An application with one boundary per assembly never shows the
    ///     difference.
    /// </remarks>
    public static (string? FullTypeName, string? ShortName) BoundaryOf(INamedTypeSymbol entity)
    {
        var declared = DeclaredOrOwningBoundaryType(entity);
        if (declared is not null)
            return (declared.ToDisplayString(), StripBoundarySuffix(declared.Name));

        var single = SingleBoundaryOf(entity.ContainingAssembly);
        if (single is null)
            return (null, null);

        var plain = StripGlobal(single);
        var lastDot = plain.LastIndexOf('.');
        return (plain, StripBoundarySuffix(lastDot >= 0 ? plain.Substring(lastDot + 1) : plain));
    }

    /// <summary>
    ///     The same answer as <see cref="BoundaryOf" />, fully qualified.
    /// </summary>
    /// <remarks>
    ///     Two formats because two consumers want different ones and both are right: the Actions and
    ///     Endpoints models compare against names carrying <c>global::</c>, the Persistence models
    ///     against the plain display string. One rule, two methods named for the format they serve —
    ///     rather than one method whose callers each normalise, which is how the two spellings of one
    ///     name silently stopped matching once already.
    /// </remarks>
    /// <summary>
    ///     The entities the entity's boundary declares <c>[ReadAccess&lt;T&gt;]</c> to, as plain display
    ///     names — empty when the boundary cannot be found or reads nothing.
    /// </summary>
    /// <remarks>
    ///     A relation to one of these is not cross-boundary for the purpose of generating a
    ///     navigation: the target sits in the reading boundary's DbContext, read-only, and EF can join
    ///     it. Degrading such a relation to its key alone is what forced the reference application to
    ///     write the navigation by hand.
    /// </remarks>
    public static ImmutableArray<string> ReadAccessTypesOf(INamedTypeSymbol entity)
    {
        var boundary = DeclaredOrOwningBoundaryType(entity);
        if (boundary is null)
        {
            var single = SingleBoundaryOf(entity.ContainingAssembly);
            if (single is not null)
                boundary = entity.ContainingAssembly.GetTypeByMetadataName(StripGlobal(single));
        }

        if (boundary is null)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var attr in boundary.GetAttributes())
        {
            if (attr.AttributeClass is { Name: "ReadAccessAttribute", TypeArguments.Length: 1 } readAccess
                && readAccess.TypeArguments[0] is INamedTypeSymbol target)
                builder.Add(target.ToDisplayString());
        }

        return builder.ToImmutable();
    }

    public static string? QualifiedBoundaryOf(INamedTypeSymbol entity)
    {
        var declared = DeclaredOrOwningBoundaryType(entity);
        return declared is not null
            ? declared.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : SingleBoundaryOf(entity.ContainingAssembly);
    }

    /// <summary>
    ///     The entity's own <c>[BelongsTo&lt;T&gt;]</c>, else the boundary of its assembly that claims
    ///     it with <c>[Owns&lt;T&gt;]</c>. Null when neither says anything, which leaves the single
    ///     boundary of the assembly as the last answer.
    /// </summary>
    private static ITypeSymbol? DeclaredOrOwningBoundaryType(INamedTypeSymbol entity)
        => DeclaredBoundaryType(entity) ?? OwningBoundaryType(entity);

    /// <summary>
    ///     The <c>[Boundary]</c> of the entity's assembly whose <c>[Owns&lt;T&gt;]</c> names it.
    /// </summary>
    /// <remarks>
    ///     Memoised per assembly like <see cref="SingleBoundaryOf" />, and for the same reason: the walk
    ///     covers the whole namespace tree, and for a relation target that tree is a referenced
    ///     assembly's. Two boundaries claiming one entity is <c>PRAG0630</c>, reported where the
    ///     compilation is read; here the first claim answers.
    /// </remarks>
    private static INamedTypeSymbol? OwningBoundaryType(INamedTypeSymbol entity)
    {
        var assembly = entity.ContainingAssembly;
        if (assembly is null)
            return null;

        var owners = OwnershipCache.GetValue(assembly, BuildOwnershipMap);
        return owners.TryGetValue(entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var owner)
            ? owner
            : null;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAssemblySymbol, Dictionary<string, INamedTypeSymbol>>
        OwnershipCache = new();

    private static Dictionary<string, INamedTypeSymbol> BuildOwnershipMap(IAssemblySymbol assembly)
    {
        var owners = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var type in EnumerateTypes(assembly.GlobalNamespace, CancellationToken.None))
        {
            if (!IsBoundary(type))
                continue;

            foreach (var owned in CollectOwned(type))
            {
                if (!owners.ContainsKey(owned))
                    owners.Add(owned, type);
            }
        }

        return owners;
    }

    private static bool IsBoundary(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.OriginalDefinition.ToDisplayString() == BoundaryAttributeName)
                return true;
        }

        return false;
    }

    /// <summary>The boundary the entity declares with <c>[BelongsTo&lt;T&gt;]</c>, if any.</summary>
    private static ITypeSymbol? DeclaredBoundaryType(INamedTypeSymbol entity)
    {
        foreach (var attr in entity.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is not { IsGenericType: true, OriginalDefinition.Name: "BelongsToAttribute" }
                || attrClass.TypeArguments.Length != 1
                || attrClass.OriginalDefinition.ContainingNamespace?.ToDisplayString() != BelongsToNamespace)
                continue;

            return attrClass.TypeArguments[0];
        }

        return null;
    }

    private static string StripGlobal(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;

    /// <summary>
    ///     The one <c>[Boundary]</c> of an assembly, or null when it declares none or several.
    /// </summary>
    /// <remarks>
    ///     Asked from a per-symbol transform, which has no compilation to read: an entity that names no
    ///     boundary belongs to the assembly's, and where there is a choice to make the caller has been
    ///     told something explicitly anyway.
    /// </remarks>
    public static string? SingleBoundaryOf(IAssemblySymbol? assembly)
    {
        if (assembly is null)
            return null;

        if (SingleBoundaryCache.TryGetValue(assembly, out var cached))
            return cached.Value;

        var answer = new Answer(FindSingleBoundary(assembly));
        SingleBoundaryCache.Add(assembly, answer);
        return answer.Value;
    }

    /// <summary>
    ///     Memoised per assembly: the walk below covers the whole namespace tree, and for a
    ///     cross-boundary target that tree belongs to a REFERENCED assembly — much larger than the one
    ///     being compiled. Every entity of every transform asks the same question, on every keystroke.
    /// </summary>
    /// <remarks>
    ///     A weak table rather than a dictionary: an assembly symbol is a different instance in each
    ///     compilation, so a stale entry can never be answered from — it is simply never hit again, and
    ///     the old one is collected instead of held alive.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAssemblySymbol, Answer>
        SingleBoundaryCache = new();

    private sealed class Answer(string? value)
    {
        public string? Value { get; } = value;
    }

    /// <summary>
    ///     Whether the assembly declares any <c>[Boundary]</c> at all.
    /// </summary>
    /// <remarks>
    ///     <see cref="SingleBoundaryOf" /> answers <c>null</c> for two different situations — no boundary,
    ///     and more than one — and a diagnostic that cannot tell them apart says the wrong thing in one
    ///     of them. A module that declares none is a library whose host supplies the context; a module
    ///     that declares several and claims this operation with none of them is the ambiguity worth
    ///     reporting.
    /// </remarks>
    public static bool DeclaresAnyBoundary(IAssemblySymbol? assembly)
    {
        if (assembly is null)
            return false;

        if (AnyBoundaryCache.TryGetValue(assembly, out var cached))
            return cached.Value is not null;

        string? found = null;
        foreach (var type in EnumerateTypes(assembly.GlobalNamespace, CancellationToken.None))
        {
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.OriginalDefinition.ToDisplayString() == BoundaryAttributeName)
                {
                    found = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    break;
                }
            }

            if (found is not null)
                break;
        }

        AnyBoundaryCache.Add(assembly, new Answer(found));
        return found is not null;
    }

    /// <summary>Memoised for the same reason as <see cref="SingleBoundaryCache" />.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAssemblySymbol, Answer>
        AnyBoundaryCache = new();

    /// <summary>
    ///     The short names of every boundary this assembly will have — declared with
    ///     <c>[Boundary]</c>, or derived from its <c>[Module]</c> when it declares none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The catalogue a transform needs to recognise a boundary interface <b>before it exists</b>.
    ///         <c>I{ShortName}Actions</c>, <c>I{ShortName}InternalActions</c> and the sub-boundary
    ///         <c>I{ShortName}{Group}Actions</c> are all emitted later in this same compilation, so a
    ///         field typed as one of them is an error type while the field is being classified — no
    ///         interface, no attributes, no base chain, nothing to read but the name.
    ///     </para>
    ///     <para>
    ///         ⚠️ Matching by name is an anti-pattern, and the exception is narrow on purpose: the
    ///         names come from a catalogue this generator builds from the boundaries it is about to
    ///         emit, not from a convention imposed on user code, and it is consulted <b>only</b> for a
    ///         symbol that failed to resolve. A name that does resolve is classified by what it is.
    ///     </para>
    /// </remarks>
    public static ImmutableArray<BoundaryName> BoundaryNamesOf(IAssemblySymbol? assembly)
    {
        if (assembly is null)
            return ImmutableArray<BoundaryName>.Empty;

        if (ShortNamesCache.TryGetValue(assembly, out var cached))
            return cached.Value;

        var names = ImmutableArray.CreateBuilder<BoundaryName>();
        INamedTypeSymbol? moduleType = null;

        foreach (var type in EnumerateTypes(assembly.GlobalNamespace, CancellationToken.None))
        {
            foreach (var attr in type.GetAttributes())
            {
                var attrName = attr.AttributeClass?.OriginalDefinition.ToDisplayString();
                if (attrName == BoundaryAttributeName)
                    names.Add(new BoundaryName(StripBoundarySuffix(type.Name), NamespaceOf(type)));
                else if (attrName == ModuleAttributeName)
                    moduleType = type;
            }
        }

        // No [Boundary]: the module gets one named after itself, and its interface with it.
        if (names.Count == 0 && moduleType is not null
            && DefaultBoundaryTransform.FromModule(moduleType) is { } derived)
            names.Add(new BoundaryName(StripBoundarySuffix(derived.TypeName), derived.Namespace));

        var answer = new Names(names.ToImmutable());
        ShortNamesCache.Add(assembly, answer);
        return answer.Value;
    }

    private static string NamespaceOf(ISymbol type)
        => type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";

    /// <summary>A boundary this assembly declares: the short name its interfaces carry, and where they land.</summary>
    /// <remarks>
    ///     The namespace travels with the name because the two consumers are the same reading: the
    ///     classifier that recognises <c>I{ShortName}…Actions</c> on an error symbol, and the emission
    ///     that has to write it <b>qualified</b>. Recognising by name and then emitting by name is how
    ///     the generated constructor came to carry a bare type that binds only while the operation's
    ///     namespace nests under the boundary's.
    /// </remarks>
    public readonly struct BoundaryName(string shortName, string ns)
    {
        public string ShortName { get; } = shortName;

        public string Namespace { get; } = ns;
    }

    /// <summary>Memoised for the same reason as <see cref="SingleBoundaryCache" />.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAssemblySymbol, Names>
        ShortNamesCache = new();

    private sealed class Names(ImmutableArray<BoundaryName> value)
    {
        public ImmutableArray<BoundaryName> Value { get; } = value;
    }

    private static string? FindSingleBoundary(IAssemblySymbol assembly)
    {
        string? found = null;
        foreach (var type in EnumerateTypes(assembly.GlobalNamespace, CancellationToken.None))
        {
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.OriginalDefinition.ToDisplayString() != BoundaryAttributeName)
                    continue;
                if (found is not null)
                    return null;
                found = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                break;
            }
        }

        return found;
    }

    private static ImmutableArray<string> CollectOwned(INamedTypeSymbol boundary)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var attr in boundary.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;
            if (!attrClass.OriginalDefinition.ToDisplayString().StartsWith(OwnsAttributePrefix, StringComparison.Ordinal))
                continue;
            if (attrClass.TypeArguments.Length > 0)
                builder.Add(attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var member in ns.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var type in EnumerateTypes(nested, ct))
                        yield return type;
                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    break;
            }
        }
    }

    /// <summary>
    ///     The boundary's short name: the type name without its <c>Boundary</c> suffix.
    /// </summary>
    /// <remarks>
    ///     Internal rather than private because a package's entities are adopted by a boundary named
    ///     somewhere else entirely, and the short name they take has to be the one every other entity
    ///     of that boundary already carries — <c>Accounts</c>, not <c>AccountsBoundary</c>. A second
    ///     copy of this rule is how the adopted entities would land in a DbContext of their own.
    /// </remarks>
    internal static string StripBoundarySuffix(string typeName)
        => typeName.EndsWith("Boundary", StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - "Boundary".Length)
            : typeName;
}

/// <summary>One <c>[Boundary]</c> of this assembly, with what it claims.</summary>
internal sealed record DeclaredBoundary(
    string FullTypeName,
    string ShortName,
    EquatableArray<string> OwnedEntities,
    LocationInfo? LocationInfo);

/// <summary>The boundaries of this assembly, and how many <c>[Module]</c> declared them.</summary>
internal sealed record BoundaryOwnership(
    EquatableArray<DeclaredBoundary> Boundaries,
    int ModuleCount,
    bool IsDerived)
{
    public static readonly BoundaryOwnership Empty =
        new(EquatableArray<DeclaredBoundary>.Empty, 0, false);
}
