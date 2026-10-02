using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     Model representing a property mapping.
/// </summary>
internal sealed record PropertyMappingModel
{
    /// <summary>
    ///     Where the property is declared, for the diagnostics reported on it. Null when the model
    ///     was not built from a symbol.
    /// </summary>
    public LocationInfo? Location { get; init; }

    /// <summary>
    ///     The name of the target property.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The full type name of the target property.
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     Whether the target property type is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether the property is required (has required modifier).
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the property is init-only.
    /// </summary>
    public bool IsInitOnly { get; init; }

    /// <summary>
    ///     Whether the property has a private setter.
    /// </summary>
    public bool HasPrivateSetter { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Mapping Configuration
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this property is explicitly ignored ([MapIgnore]).
    /// </summary>
    public bool IsIgnored { get; init; }

    /// <summary>
    ///     The source property paths (from [MapProperty]).
    ///     Multiple paths indicate concatenation.
    /// </summary>
    public EquatableArray<string> SourcePaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The format string (from [MapProperty(Format = "...")]).
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    ///     The joined expression as a <b>projection</b> has to express it, or <c>null</c> when the
    ///     projection can use <see cref="SourceExpression" /> unchanged.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The two paths do not agree on what an enum concatenates to. In memory
    ///     <c>string + enum</c> calls <c>ToString()</c> and gives the member's name; the same expression
    ///     translated to SQL concatenates the column, which is the number it is stored as. One
    ///     declaration answered two ways, and the projected answer was <b>wrong rather than missing</b>.
    ///     <para>
    ///         So the join is composed twice, in one place, and the projection's form renders each enum
    ///         part as a chain of comparisons — a <c>CASE</c> once EF translates it. Null here means the
    ///         join has no enum in it and the two forms are the same string.
    ///     </para>
    /// </remarks>
    public string? ProjectionSourceExpression { get; init; }

    /// <summary>
    ///     A <c>[Projectable]</c> member at the end of a path through a navigation, read plain —
    ///     <c>entity.Customer.OpenBalance</c>, the form the projection has once its <c>?.</c> are
    ///     turned into null checks. Null when the path ends on anything else.
    /// </summary>
    /// <remarks>
    ///     Replaced in the finished projection by <see cref="ProjectableReadBody" />, so every null
    ///     check and default the projection writes around the read stays as it is.
    /// </remarks>
    public string? ProjectableRead { get; init; }

    /// <summary>The body of the member in <see cref="ProjectableRead" />, over the same navigation.</summary>
    public string? ProjectableReadBody { get; init; }

    /// <summary>
    ///     The navigations a <c>[Projectable]</c> source member reads, with the path that reaches them:
    ///     the in-memory mapping reads its getter, and the getter walks them, so they are loaded too.
    ///     Empty for any other member.
    /// </summary>
    public EquatableArray<string> ProjectableNavigations { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The separator for concatenation (from [MapProperty(Separator = "...")]).
    /// </summary>
    public string Separator { get; init; } = " ";

    /// <summary>
    ///     The default value (from [MapProperty(Default = ...)]).
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    ///     The target property path for MapTo scenarios (from [MapProperty(Target = "...")]).
    ///     Supports nested paths like "Customer.Name" for mapping to nested entity properties.
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    ///     Intermediate navigation prefixes of a dotted <see cref="TargetPath"/>, in order
    ///     (e.g. <c>["Customer", "Customer.Address"]</c> for <c>Customer.Address.City</c>).
    ///     Used to emit null-guards so the write path can't NRE on a null navigation.
    /// </summary>
    public EquatableArray<string> TargetPathIntermediates { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     True when every intermediate navigation type has an accessible parameterless constructor —
    ///     the template then emits <c>entity.X ??= new();</c> per prefix. False → the assignment is
    ///     wrapped in a null-check instead (no NRE, value skipped when the navigation is null).
    /// </summary>
    public bool TargetIntermediatesConstructible { get; init; } = true;

    /// <summary>
    ///     True when some intermediate of <see cref="TargetPath"/> is an entity — a row of its own,
    ///     with an identity and a lifetime the update did not create.
    /// </summary>
    /// <remarks>
    ///     What separates <c>Shipment.Recipient.City</c>, where the recipient is a shape the shipment
    ///     owns, from <c>Reservation.Property.Name</c>, where the property is a row that exists
    ///     independently. Creating the first while building an aggregate is right; creating the second
    ///     because someone wrote a dotted path is an insert nobody asked for.
    /// </remarks>
    public bool TargetPathCrossesEntity { get; init; }

    /// <summary>
    ///     Whether the entity's property is written through its generated <c>Set{Name}</c> rather than
    ///     assigned.
    /// </summary>
    /// <remarks>
    ///     An entity that keeps its state private — the shape this framework recommends and its own
    ///     generator produces setters for — cannot be assigned from outside. The write path emitted a
    ///     plain assignment regardless, so <c>[MapTo&lt;T&gt;]</c> over such an entity produced
    ///     <c>CS0272</c> in a file the author cannot open. It never showed because every
    ///     <c>[MapTo]</c> in the reference application targets a plain class with public setters.
    /// </remarks>
    public bool TargetHasNonPublicSetter { get; init; }

    /// <summary>
    ///     The entity property this writes to has no setter at all — a computed value.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the same as a non-public setter, which is written through the generated
    ///     <c>Set{Name}</c>. There is nothing to write here, and emitting the assignment anyway was
    ///     <c>CS0200</c> inside a file the author cannot open. Reported as <c>PRAG0336</c>, which
    ///     names the remedy: <c>[MapIgnore(MappingDirection.ToEntity)]</c> says out loud what the
    ///     shape already implies.
    /// </remarks>
    public bool TargetIsReadOnly { get; init; }

    /// <summary>
    ///     What a string that names no member of the target enum becomes: <c>Throw</c> (the
    ///     default), <c>Default</c> or <c>Null</c>.
    /// </summary>
    /// <remarks>
    ///     A string rather than the enum, like the two strategies beside it: the template writes the
    ///     member's name into generated code, and the model crosses into a project that does not
    ///     reference <c>Pragmatic.Mapping</c>.
    /// </remarks>
    public string EnumOnUnknown { get; init; } = "Throw";

    /// <summary>
    ///     Two enums are paired by value — a cast — rather than by member name.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It also removes what the by-name form checks: <c>PRAG0328</c> exists because a member
    ///     the other side does not have cannot be mapped, and a cast has nothing to refuse.
    /// </remarks>
    public bool EnumMatchesByValue { get; init; }

    /// <summary>
    ///     The entity property's type, fully qualified — the target of a write-side conversion.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not <see cref="SourcePropertyType" />, which is the readable form. A cast written into
    ///     generated code has to name the type the way generated code names every other type:
    ///     <c>global::</c>-qualified, because the file has no <c>using</c> the author controls and a
    ///     simple name resolves against whatever happens to be in scope.
    /// </remarks>
    public string? TargetFullTypeName { get; init; }

    /// <summary>
    ///     This property's own type, fully qualified — the target of a read-side conversion.
    /// </summary>
    /// <remarks>
    ///     Same reason as <see cref="TargetFullTypeName" />: a cast or a type argument written into
    ///     generated code names the type the way the rest of the file does. It never showed before
    ///     because the conversions that existed cast to <c>int</c> and friends, which have no
    ///     namespace to be wrong about.
    /// </remarks>
    public string? PropertyFullTypeName { get; init; }

    /// <summary>
    ///     The child is a <c>Mutation&lt;TEntity&gt;</c>, not a DTO with <c>[MapTo]</c>.
    /// </summary>
    /// <remarks>
    ///     It changes how the child is <b>built</b>: a mutation has no <c>ToEntity()</c> — the invoker
    ///     loads the entity — so a new child comes from the entity's factory and is filled by
    ///     <c>ApplyToEntity</c>. It is the same shape <c>ChildWritingTemplate</c> uses on the Actions
    ///     side.
    /// </remarks>
    public bool ChildIsMutation { get; init; }

    /// <summary>The entity behind the mutation child, fully qualified: the factory comes from it.</summary>
    public string? ChildEntityFullTypeName { get; init; }

    /// <summary>
    ///     The enum members that carry a wire name of their own, as <c>Member=alias</c> pairs.
    /// </summary>
    /// <remarks>
    ///     Empty for every enum whose members are named on the wire as they are named in C#, which is
    ///     nearly all of them: the switch is emitted only where there is something to translate.
    /// </remarks>
    public EquatableArray<string> EnumAliases { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The target property is <c>init</c>-only, so it can be written only in an object initializer.
    /// </summary>
    public bool TargetIsInitOnly { get; init; }

    /// <summary>
    ///     Set when a segment of <see cref="TargetPath"/> does not exist on the entity: the offending
    ///     segment name, reported as PRAG0302. The mapping is skipped (Resolution = None).
    /// </summary>
    public string? TargetPathInvalidSegment { get; init; }

    /// <summary>
    ///     Set when an explicit <c>[MapProperty("...")]</c> source path does not resolve on the source
    ///     type: the offending path, reported as PRAG0302. The mapping is skipped (Resolution = None).
    /// </summary>
    public string? ExplicitSourcePathInvalid { get; init; }

    /// <summary>
    ///     Set when the reason <see cref="ExplicitSourcePathInvalid" /> did not resolve is a relation
    ///     that crosses a boundary: PRAG0334 is reported in place of PRAG0302.
    /// </summary>
    /// <remarks>
    ///     Three strings rather than the symbol, because the model has to stay value-equatable for the
    ///     incremental pipeline: the entity on the far side, and the two boundaries.
    /// </remarks>
    public string? CrossBoundaryOtherEntity { get; init; }

    /// <inheritdoc cref="CrossBoundaryOtherEntity" />
    public string? CrossBoundaryOwn { get; init; }

    /// <inheritdoc cref="CrossBoundaryOtherEntity" />
    public string? CrossBoundaryOther { get; init; }

    /// <summary>
    ///     True when the property carries both [MapIgnore] and [MapProperty] (PRAG0314). Ignore wins.
    /// </summary>
    public bool HasConflictingAttributes { get; init; }

    /// <summary>
    ///     Set when a nested DTO's <c>[MapFrom&lt;T&gt;]</c> source type is unrelated to the actual
    ///     source navigation type (PRAG0315): the nested DTO's declared source type name.
    /// </summary>
    public string? NestedDtoMismatchSource { get; init; }

    /// <summary>
    ///     True when the property resolved by direct name match but a flattening convention would also
    ///     match (PRAG0323). Direct match wins; the warning invites an explicit [MapProperty].
    /// </summary>
    public bool AmbiguousWithConvention { get; init; }

    /// <summary>
    ///     True when nested projection inlining for this property was truncated by the MaxDepth cap
    ///     (PRAG0327): deeper members are omitted from the projection expression.
    /// </summary>
    public bool ProjectionDepthCapped { get; init; }

    /// <summary>
    ///     Write-side materialization kind: the ENTITY property's collection kind (ToEntity/ApplyTo must
    ///     materialize what the entity declares, not what the DTO declares). None → fall back to
    ///     <see cref="CollectionKind"/>.
    /// </summary>
    public CollectionKind TargetCollectionKind { get; init; } = CollectionKind.None;

    /// <summary>
    ///     Member names for an enum→enum conversion (validated: every source member exists on the
    ///     target). The template emits a by-name switch expression from these.
    /// </summary>
    public EquatableArray<string> EnumToEnumMembers { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Set when an enum→enum mapping has a source member with no same-named target member
    ///     (PRAG0328): the offending member name. The mapping is skipped.
    /// </summary>
    public string? EnumMemberMismatch { get; init; }

    /// <summary>
    ///     Predicate method name from [MapCondition]: the property maps only when
    ///     <c>{Method}(entity)</c> returns true, otherwise it keeps its default.
    /// </summary>
    public string? ConditionMethod { get; init; }

    /// <summary>
    ///     True when the [MapCondition] method is missing or has the wrong shape — not a static bool
    ///     taking the source type (PRAG0329).
    /// </summary>
    public bool ConditionMethodInvalid { get; init; }

    /// <summary>
    ///     The <c>[MapCondition]</c> predicate's body over the projection's row (<c>entity</c>), when the
    ///     predicate is an expression-bodied method the projection can inline; null otherwise, and then
    ///     the projection maps the property unconditionally (PRAG0332).
    /// </summary>
    public string? ConditionProjectionBody { get; init; }

    /// <summary>
    ///     A nested DTO built from the row itself: its <c>[MapFrom&lt;T&gt;]</c> is the source entity and no
    ///     member of the source carries the property's name. Read from <c>entity</c>, with no
    ///     null check — the row is there.
    /// </summary>
    public bool IsSameRow { get; init; }

    /// <summary>
    ///     A nested DTO the author declared non-nullable, in a nullable context: the mapping does not
    ///     assign it null. Oblivious code keeps the null check.
    /// </summary>
    public bool IsDeclaredNonNull { get; init; }

    /// <summary>
    ///     True when the [MapConverter] type does not implement <c>IValueConverter&lt;,&gt;</c> (PRAG0305).
    /// </summary>
    public bool ConverterMissingInterface { get; init; }

    /// <summary>
    ///     True when the [MapConverter] type has no public parameterless constructor (PRAG0306).
    /// </summary>
    public bool ConverterMissingParameterlessCtor { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Converter
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     The converter type full name (from [MapConverter]).
    /// </summary>
    public string? ConverterType { get; init; }

    /// <summary>
    ///     Whether this property uses a converter.
    /// </summary>
    public bool HasConverter => !string.IsNullOrEmpty(ConverterType);

    // ═══════════════════════════════════════════════════════════════════════════
    // Mapping Resolution
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     How this property was resolved (explicit, direct, flattening, concatenation).
    /// </summary>
    public MappingResolution Resolution { get; init; }

    /// <summary>
    ///     The resolved source expression (e.g., "entity.Address.City").
    /// </summary>
    public string? SourceExpression { get; init; }

    /// <summary>
    ///     The source property type (for type compatibility checks).
    /// </summary>
    public string? SourcePropertyType { get; init; }

    /// <summary>
    ///     Whether the source type is nullable.
    /// </summary>
    public bool SourceIsNullable { get; init; }

    /// <summary>
    ///     Whether this path reads through a value that lives in the entity's own row — a
    ///     <c>[ValueObject]</c> or a <c>Money</c>, which the persistence generator maps as an EF Core
    ///     complex type.
    /// </summary>
    /// <remarks>
    ///     It is not a navigation, and three things follow. The path is read with <c>.</c>,
    ///     because a non-nullable one is never null once the entity is materialised. It needs no
    ///     "not loaded" guard, and on a <c>struct</c> such as <c>Money</c> that guard is not even legal
    ///     — <c>is null</c> on a non-nullable value type does not compile. And it is not a required
    ///     navigation: <c>Include("Amount")</c> on a complex type is not a thing to ask EF for.
    /// </remarks>
    public bool SourceIsSameRowValue { get; init; }

    /// <summary>
    ///     Write side ([MapTo]): true when the DTO property is a nullable value type (<c>Nullable&lt;T&gt;</c>)
    ///     and the target entity property is a non-nullable value type. ToEntity/ApplyTo must unwrap with
    ///     <c>.GetValueOrDefault()</c> — a bare <c>this.X</c> would be <c>int?</c> → <c>int</c> = CS0266.
    /// </summary>
    public bool NeedsNullableValueUnwrap { get; init; }

    /// <summary>
    ///     The automatic conversion to apply (if any).
    /// </summary>
    public ConversionKind Conversion { get; init; }

    /// <summary>
    ///     Whether this property requires type conversion.
    /// </summary>
    public bool RequiresConversion => Conversion != ConversionKind.None;

    /// <summary>
    ///     Whether the source type is an enum (for StringToEnum conversion).
    /// </summary>
    public bool SourceIsEnum { get; init; }

    /// <summary>
    ///     Whether the target type is an enum (for StringToEnum conversion).
    /// </summary>
    public bool TargetIsEnum { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Nested & Collection Mapping
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this is a nested DTO that requires recursive mapping.
    /// </summary>
    public bool IsNestedDto { get; init; }

    /// <summary>
    ///     The nested DTO type name (if IsNestedDto).
    /// </summary>
    public string? NestedDtoType { get; init; }

    /// <summary>
    ///     The collection type (List, Array, IEnumerable, etc.), or null if not a collection.
    /// </summary>
    public CollectionKind CollectionKind { get; init; }

    /// <summary>
    ///     The element type for collections.
    /// </summary>
    public string? ElementType { get; init; }

    /// <summary>
    ///     Whether the element type is a DTO that needs mapping.
    /// </summary>
    public bool IsElementDto { get; init; }

    /// <summary>
    ///     Whether the element type is a simple type (string, int, etc.).
    /// </summary>
    public bool IsElementSimple { get; init; }

    /// <summary>
    ///     The element DTO type for collection mapping.
    /// </summary>
    public string? ElementDtoType { get; init; }

    /// <summary>
    ///     How this collection is written back to the entity, and by what its elements are matched.
    ///     Null when the property is not a collection of DTOs.
    /// </summary>
    /// <remarks>
    ///     Before this, a collection was written as
    ///     <c>entity.Items = this.Items.Select(x =&gt; x.ToEntity()).ToList()</c> — correct when creating,
    ///     and on an update a wholesale replacement that discarded every existing child along with its
    ///     identity, audit columns and soft-delete state.
    /// </remarks>
    public CollectionWriteModel? CollectionWrite { get; init; }

    /// <summary>
    ///     How this single reference navigation is written back: <c>Merge</c>, <c>Detach</c>,
    ///     <c>Replace</c> or <c>Ignore</c>.
    /// </summary>
    /// <remarks>
    ///     A string rather than the enum, for the same reason the collection's strategy is one: this
    ///     model crosses into the template, which writes the member's name into the generated code.
    ///     <c>Merge</c> for every property that does not declare otherwise, including the ones that
    ///     are not navigations at all — the templates only ask when they are about to write one.
    /// </remarks>
    public string ReferenceStrategy { get; init; } = "Merge";

    /// <summary>
    ///     The navigation whose links this list of keys chooses, from <c>[LinkIds]</c>; null when the
    ///     property is not one.
    /// </summary>
    public string? LinkNavigation { get; init; }

    /// <summary>The related entity's key property, from <c>[LinkIds(Key = …)]</c>.</summary>
    public string LinkKey { get; init; } = "PersistenceId";

    /// <summary>The related entity, fully qualified — what a stub is built from.</summary>
    public string? LinkEntityFullTypeName { get; init; }

    /// <summary>How the set of links is written: the four of <c>CollectionStrategy</c>.</summary>
    public string LinkStrategy { get; init; } = "Sync";

    // ═══════════════════════════════════════════════════════════════════════════
    // Dictionary Mapping
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this is a dictionary property.
    /// </summary>
    public bool IsDictionary { get; init; }

    /// <summary>
    ///     The dictionary key type.
    /// </summary>
    public string? DictionaryKeyType { get; init; }

    /// <summary>
    ///     The dictionary value type.
    /// </summary>
    public string? DictionaryValueType { get; init; }

    /// <summary>
    ///     Whether the dictionary value is a simple type (deep copy supported).
    /// </summary>
    public bool IsDictionaryValueSimple { get; init; }

    /// <summary>
    ///     The DTO type for dictionary values (if value is a DTO that needs mapping).
    /// </summary>
    public string? DictionaryValueDtoType { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // ID Property Handling
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this is an ID property (named Id or {EntityName}Id).
    /// </summary>
    public bool IsIdProperty { get; init; }

    /// <summary>
    ///     Whether to force include this ID in ToEntity (via [MapProperty]).
    /// </summary>
    public bool ForceIncludeId { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Projection Support
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this property can be included in a projection (SQL-translatable).
    /// </summary>
    public bool IsSqlTranslatable { get; init; }

    /// <summary>
    ///     Whether the projection carries it although SQL cannot translate it: a <c>Format</c> or a
    ///     <c>[MapConverter]</c> over a scalar read straight off the row, computed on the client in the last
    ///     step of the read, as <c>FromEntity</c> computes it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The projection is the executor's top-level <c>Select</c>, the one place EF Core evaluates what it
    ///     cannot translate — as it already does for a <c>LocalizedString</c>'s <c>.Value</c>. Left out, the
    ///     member answered with the DTO's initialiser on every query. Not through a navigation that
    ///     may be null (<c>?.</c> in the source): the client step would dereference it, and an expression tree
    ///     cannot say <c>?.</c>.
    /// </remarks>
    public bool IsComputedAfterTheRead =>
        !IsSqlTranslatable
        && ((HasConverter && SourcePropertyType is not null) || (!string.IsNullOrEmpty(Format) && RequiresConversion))
        && SourceExpression is { } source && !source.Contains("?.")
        && !IsNestedDto && !IsElementDto && !IsDictionary && CollectionKind == CollectionKind.None;

    /// <summary>
    ///     The projection expression (if different from source expression).
    /// </summary>
    public string? ProjectionExpression { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Nested Projection Inlining (for EF Core)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Property mappings for inline projection of nested DTOs.
    ///     Maps target property name to source property path for direct matches.
    /// </summary>
    public EquatableArray<NestedPropertyMapping> NestedProjectionMappings { get; init; } =
        EquatableArray<NestedPropertyMapping>.Empty;

    /// <summary>
    ///     Property mappings for inline projection of collection element DTOs.
    /// </summary>
    public EquatableArray<NestedPropertyMapping> ElementProjectionMappings { get; init; } =
        EquatableArray<NestedPropertyMapping>.Empty;

    /// <summary>
    ///     True when nested DTO inlining skipped one or more mapping forms
    ///     (converter, concatenation, format string, flattening) that the inline projection
    ///     initializer cannot represent. Surfaced as PRAG0326 so the downgrade is not silent.
    /// </summary>
    public bool HasDroppedNestedProjectionMappings { get; init; }
}

/// <summary>
///     Represents a property mapping for nested DTO projection inlining.
///     Supports recursive nesting for deep hierarchies (3+ levels).
/// </summary>
internal sealed record NestedPropertyMapping
{
    /// <summary>
    ///     The source paths of a multi-path join whose type is an <c>enum</c>, when there are any.
    /// </summary>
    /// <remarks>
    ///     Empty for every other mapping, and the renderer falls back to plain concatenation when it is
    ///     — which is what every mapping without an enum in it wants, and what all of them got before.
    ///     Only the branch that builds a multi-path mapping can fill this, and it is the only one that
    ///     reads it.
    /// </remarks>
    public EquatableArray<EnumJoinPart> EnumJoinParts { get; init; } = EquatableArray<EnumJoinPart>.Empty;

    /// <summary>
    ///     The target property name in the DTO.
    /// </summary>
    public required string TargetPropertyName { get; init; }

    /// <summary>
    ///     The source property name in the entity.
    /// </summary>
    public required string SourcePropertyName { get; init; }

    /// <summary>
    ///     The full type name of the property.
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     Whether this property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     The body of the source member when it is <c>[Projectable]</c>, over
    ///     <c>ProjectableBody.PortableSource</c>: the initializer writes it over whatever reaches the
    ///     nested source, instead of the getter EF Core would run in memory. Null otherwise.
    /// </summary>
    public string? ProjectableBody { get; init; }

    /// <summary>
    ///     A <c>string</c> read from a <c>LocalizedString</c>: the initializer reads its <c>.Value</c>, in
    ///     the culture of the request, as the top-level projection does.
    /// </summary>
    public bool ReadsLocalizedValue { get; init; }

    /// <summary>
    ///     Whether the source member can be null — for the guard a <c>.Value</c> read needs, and for the
    ///     default a non-nullable target gets.
    /// </summary>
    public bool SourceIsNullable { get; init; }

    /// <summary>The source member's type, for the default a non-nullable target gets; null when unknown.</summary>
    public string? SourcePropertyType { get; init; }

    /// <summary>Whether the source member is an enum, for the same default.</summary>
    public bool SourceIsEnum { get; init; }

    /// <summary>
    ///     A nested DTO declared non-nullable in a nullable context: inlined without the null check that
    ///     would assign it null.
    /// </summary>
    public bool IsDeclaredNonNull { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Deep Nesting Support
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this property is a nested DTO requiring recursive inlining.
    /// </summary>
    public bool IsNestedDto { get; init; }

    /// <summary>
    ///     The nested DTO type (fully qualified) if IsNestedDto is true.
    /// </summary>
    public string? NestedDtoType { get; init; }

    /// <summary>
    ///     Recursive mappings for the nested DTO properties.
    /// </summary>
    public EquatableArray<NestedPropertyMapping> NestedMappings { get; init; } =
        EquatableArray<NestedPropertyMapping>.Empty;

    /// <summary>
    ///     Whether this property is a collection of DTOs.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The collection kind (List, Array, etc.) if IsCollection is true.
    /// </summary>
    public CollectionKind CollectionKind { get; init; }

    /// <summary>
    ///     The element DTO type for collections.
    /// </summary>
    public string? ElementDtoType { get; init; }

    /// <summary>
    ///     Recursive mappings for collection element DTOs.
    /// </summary>
    public EquatableArray<NestedPropertyMapping> ElementMappings { get; init; } =
        EquatableArray<NestedPropertyMapping>.Empty;

    /// <summary>
    ///     Explicit source paths from [MapProperty] — a single dotted path (flattening) or several
    ///     (concatenation). Both are expression-tree-safe, so they inline into projections. Empty →
    ///     use <see cref="SourcePropertyName"/>.
    /// </summary>
    public EquatableArray<string> SourcePaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Concatenation separator (from [MapProperty(Separator = ...)]).</summary>
    public string Separator { get; init; } = " ";
}
