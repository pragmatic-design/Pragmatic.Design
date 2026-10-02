using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     Model representing a complete mapping definition for a type.
///     Used for incremental generator caching.
/// </summary>
internal sealed record MappingModel
{
    /// <summary>
    ///     The namespace of the target DTO type.
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     The name of the target DTO type (e.g., "UserDto").
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The full type name including namespace.
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>
    ///     The accessibility modifier (public, internal, etc.).
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     The type keyword (class, record, struct, record struct).
    /// </summary>
    public required string TypeKind { get; init; }

    /// <summary>
    ///     Whether this is a record type.
    /// </summary>
    public bool IsRecord { get; init; }

    /// <summary>
    ///     Whether this is a value type (struct).
    /// </summary>
    public bool IsValueType { get; init; }

    /// <summary>
    ///     Whether this DTO inherits another DTO that this generator also writes mapping members onto.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The template writes four <c>static</c> properties — <c>Selector</c>, <c>Projection</c>,
    ///     <c>RequiredNavigations</c>, <c>WrittenNavigations</c> — and a static property of the same
    ///     name on the base is hidden whether the generator says so or not. Saying so is what makes it
    ///     legal: <c>CS0108</c> is a warning by default, and an <b>error</b> under
    ///     <c>--warnaserror</c>, so without this a <c>[MapFrom]</c> DTO inheriting another would
    ///     not build — which is the exact shape <c>[MapDerived]</c> requires and <c>PRAG0330</c>
    ///     enforces.
    /// </remarks>
    public bool InheritsAMappedDto { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Source Type Info (for [MapFrom])
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     The fully qualified name of the source type (entity).
    /// </summary>
    public string? SourceTypeFullName { get; init; }

    /// <summary>
    ///     The simple name of the source type.
    /// </summary>
    public string? SourceTypeName { get; init; }

    /// <summary>
    ///     Whether the source type is a value type (struct).
    ///     Used to skip Ensure.ThrowIfNull for struct source types.
    /// </summary>
    public bool SourceTypeIsValueType { get; init; }

    /// <summary>
    ///     Whether this type has [MapFrom] attribute.
    /// </summary>
    public bool HasMapFrom { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Target Type Info (for [MapTo])
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     The fully qualified name of the target type (entity).
    /// </summary>
    public string? TargetTypeFullName { get; init; }

    /// <summary>
    ///     The simple name of the target type.
    /// </summary>
    public string? TargetTypeName { get; init; }

    /// <summary>
    ///     Whether this type has [MapTo] attribute.
    /// </summary>
    public bool HasMapTo { get; init; }

    /// <summary>
    ///     The model comes from a <c>Mutation&lt;TEntity&gt;</c>, not from a written <c>[MapTo]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A mutation <b>is</b> a set of properties written onto an entity, so Mapping already
    ///         knows how to do it — with converters, renames and dotted targets. What changes is how
    ///         much to emit: a mutation needs only the body of the write, because <c>ToEntity</c> has
    ///         no meaning (the invoker loads the entity) and <c>WrittenNavigations</c> belongs to
    ///         Actions, which composes the children's lists.
    ///     </para>
    ///     <para>
    ///         ⚠️ It is also what keeps the two generators out of each other's way: two generators
    ///         emitting the same member on the same partial type give <c>CS0102</c>. They do not
    ///         coordinate — they read the same answer.
    ///     </para>
    /// </remarks>
    public bool IsMutationBody { get; init; }

    /// <summary>
    ///     Whether the [MapTo] target type is a value type (affects the BeforeToEntity hook shape).
    /// </summary>
    public bool TargetTypeIsValueType { get; init; }

    /// <summary>
    ///     Whether this type also has [Mutation&lt;T&gt;] attribute (from Pragmatic.Persistence).
    ///     When true, Mutation generator will delegate to ApplyTo().
    /// </summary>
    public bool HasMutationAttribute { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Properties
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     All property mappings for this type. For a bidirectional DTO (both [MapFrom] and [MapTo])
    ///     these are the READ-side ([MapFrom]) mappings used by FromEntity/projection.
    /// </summary>
    public EquatableArray<PropertyMappingModel> Properties { get; init; } = EquatableArray<PropertyMappingModel>.Empty;

    /// <summary>
    ///     Write-side ([MapTo]) property mappings, populated only when a DTO has BOTH
    ///     [MapFrom] and [MapTo]. The merged model keeps the read-side <see cref="Properties" /> for
    ///     FromEntity/projection; ToEntity/ApplyTo must use these write-side mappings so ID exclusion,
    ///     converters and nested target paths follow [MapTo] semantics instead of the read model.
    ///     Empty for standalone [MapTo] (which uses <see cref="Properties" /> directly).
    /// </summary>
    public EquatableArray<PropertyMappingModel> WriteProperties { get; init; } = EquatableArray<PropertyMappingModel>.Empty;

    /// <summary>
    ///     The property mappings the write path (ToEntity/ApplyTo) must use:
    ///     <see cref="WriteProperties" /> when present (bidirectional DTO), otherwise
    ///     <see cref="Properties" /> (standalone [MapTo]).
    /// </summary>
    public EquatableArray<PropertyMappingModel> EffectiveWriteProperties =>
        WriteProperties.IsDefaultOrEmpty ? Properties : WriteProperties;

    // ═══════════════════════════════════════════════════════════════════════════
    // Projection
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this type has [GenerateProjection] attribute.
    /// </summary>
    public bool GenerateProjection { get; init; }

    /// <summary>
    ///     Whether this type has [GenerateBodyOnlyVariant] attribute.
    ///     When true, generates an additional FromEntityBodyOnly() method
    ///     that maps only scalar properties (no collections, nested DTOs, dictionaries).
    /// </summary>
    public bool GenerateBodyOnlyVariant { get; init; }

    /// <summary>
    ///     Navigation paths required by this DTO's mapping.
    ///     Inferred from multi-segment source paths, nested DTOs, and collection DTOs.
    ///     Example: ["Customer", "Customer.Address", "Lines"]
    /// </summary>
    public EquatableArray<string> RequiredNavigations { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether this DTO requires any navigation paths to be loaded.
    /// </summary>
    public bool HasRequiredNavigations => RequiredNavigations.Length > 0;

    /// <summary>
    ///     Navigation paths this DTO <em>writes</em>, deep and prefixed.
    ///     Example: ["Lines", "Lines.Allocations"]
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not the same set as <see cref="RequiredNavigations" />, and the difference is a
    ///         correctness one rather than an optimisation. That list answers "what must be loaded to
    ///         build this DTO" and is derived from the read model; this one answers "what must be
    ///         loaded before writing this DTO" and is derived from the write model.
    ///     </para>
    ///     <para>
    ///         For a bidirectional DTO the two sets differ whenever the shapes differ, because
    ///         the write path uses the <c>[MapTo]</c> property model. A navigation
    ///         written and not read is absent from the read list, so a caller including from it would
    ///         merge into a collection nobody loaded.
    ///     </para>
    ///     <para>
    ///         Emitted on a <c>[MapTo]</c> type, empty list included, so generated code elsewhere can
    ///         name it without being able to see this model.
    ///     </para>
    /// </remarks>
    public EquatableArray<string> WrittenNavigations { get; init; } = EquatableArray<string>.Empty;

    // ═══════════════════════════════════════════════════════════════════════════
    // Circular Reference Tracking
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this mapping has circular references that need instance tracking.
    /// </summary>
    public bool HasCircularReferences { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Constructor Info (for [MapTo])
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Constructor parameters for the target type (when using [MapTo]).
    /// </summary>
    public EquatableArray<ConstructorParameterModel> ConstructorParameters { get; init; }
        = EquatableArray<ConstructorParameterModel>.Empty;

    /// <summary>
    ///     Whether a constructor was explicitly selected with [MapConstructor].
    /// </summary>
    public bool HasExplicitConstructor { get; init; }

    /// <summary>
    ///     The target's generated <c>Create()</c> takes no parameters, so <c>ToEntity</c> can build
    ///     through the factory instead of the constructor.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Predicted, like the mutation path predicts it, and for the same reason: the factory is
    ///     written by the persistence generator and no other feature sees it. False means «not sure»,
    ///     and the write falls back to an object initializer.
    ///     <para>
    ///         It exists so that a DTO and a mutation build the same entity the same way. The mutation
    ///         goes through the factory and applies <c>[DefaultValue]</c>; a <c>ToEntity</c> that did
    ///         not would give the same row a different birth depending on which door it came through.
    ///     </para>
    /// </remarks>
    public bool TargetHasParameterlessFactory { get; init; }

    /// <summary>
    ///     Constructor parameters for the DTO itself, used by <c>FromEntity</c> when the DTO has no public
    ///     parameterless constructor (a positional <c>record</c>). Empty for DTOs constructed with an object
    ///     initializer. Each parameter's <see cref="ConstructorParameterModel.MatchingPropertyName"/> is the
    ///     mapped property whose value feeds the argument.
    /// </summary>
    public EquatableArray<ConstructorParameterModel> DtoConstructorParameters { get; init; }
        = EquatableArray<ConstructorParameterModel>.Empty;

    // ═══════════════════════════════════════════════════════════════════════════
    // Customization
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether the developer has implemented BeforeMapping partial method.
    /// </summary>
    public bool HasBeforeMapping { get; init; }

    /// <summary>
    ///     Whether the developer has implemented CustomizeMapping partial method.
    /// </summary>
    public bool HasCustomizeMapping { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Diagnostic Flags
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Where the type is declared, for the diagnostics reported on it. Null for a mapping another
    ///     feature describes as a model, which has no declaration to point at.
    /// </summary>
    /// <remarks>
    ///     Reported at <c>Location.None</c>, an error would say "which type, not which line"; for
    ///     <c>PRAG0325</c>, which is <c>Hidden</c>, it would mean nowhere at all. Excluded from equality
    ///     by <see cref="LocationInfo" /> itself, so the model still caches.
    /// </remarks>
    public LocationInfo? Location { get; init; }

    /// <summary>
    ///     True if the type is not declared as partial (PRAG0300).
    /// </summary>
    public bool MissingPartial { get; init; }

    /// <summary>
    ///     The type argument of <c>[MapFrom&lt;T&gt;]</c> / <c>[MapTo&lt;T&gt;]</c> is not a type this
    ///     generator can name.
    /// </summary>
    /// <remarks>
    ///     It happens when <c>T</c> is the containing type's own type parameter: a generic DTO written
    ///     <c>Dto&lt;T&gt; : … [MapFrom&lt;T&gt;]</c> has no source shape until it is closed, and there
    ///     is nothing to read properties from. PRAG0338 says so; a silent <c>return null</c> would leave
    ///     the attribute compiling, no mapper appearing, and the build green.
    /// </remarks>
    public bool UnusableGenericArgument { get; init; }


    /// <summary>
    ///     Source-entity property names not consumed by any DTO mapping (PRAG0325, Hidden — reverse
    ///     coverage; opt-in via .editorconfig severity bump).
    /// </summary>
    public EquatableArray<string> UnmappedSourceProperties { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Maximum nesting depth inlined into the projection (from [GenerateProjection(MaxDepth = ...)],
    ///     default 5). Exceeding it truncates the nested inlining and reports PRAG0327.
    /// </summary>
    public int ProjectionMaxDepth { get; init; } = 5;

    /// <summary>
    ///     Polymorphic dispatch pairs from [MapDerived&lt;TSource, TDto&gt;], in declaration order.
    ///     FromEntity type-switches on these before the base mapping.
    /// </summary>
    public EquatableArray<DerivedMappingModel> DerivedMappings { get; init; } = EquatableArray<DerivedMappingModel>.Empty;

    /// <summary>
    ///     [MapDerived] pairs whose types don't satisfy the contract (derived source must derive the
    ///     [MapFrom] source; derived DTO must derive this DTO) — the offending DTO type names (PRAG0330).
    /// </summary>
    public EquatableArray<string> InvalidDerivedMappings { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether the compilation references EF Core, so a load-aware overload can be written.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The merge cannot see whether the collection it is merging into was ever
    ///     loaded — it receives an <c>ICollection</c>, and in EF an unloaded collection and an
    ///     empty one are the same object. EF itself can see it, so where EF is present the
    ///     generator writes a second entry point that asks before writing. Measured: without
    ///     it, sending the same two rows back writes four.
    /// </remarks>
    public bool HasEfCore { get; init; }
}