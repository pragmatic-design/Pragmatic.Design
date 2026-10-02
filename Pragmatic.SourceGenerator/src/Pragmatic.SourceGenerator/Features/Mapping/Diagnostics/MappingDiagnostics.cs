using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Mapping source generator.
///     ID Range: PRAG0300-0399
/// </summary>
internal static class MappingDiagnostics
{
    private const string Category = "Pragmatic.Mapping";

    // ═══════════════════════════════════════════════════════════════════════════
    // PRAG0300-0309: Basic Validation Errors
    // ═══════════════════════════════════════════════════════════════════════════

    // PRAG0300 (type must be partial) is the companion analyzer's, which reports it on the declaration
    // (NotPartialDiagnosticDescriptors); the generator skips the type silently.

    // PRAG0301 was SourceTypeNotFound - removed (unreachable by construction: [MapFrom<T>]/[MapTo<T>]
    // are generic attributes, so an unresolvable T is a CS0246 on the user's own code, not ours)

    public static readonly DiagnosticDescriptor PropertyNotFound = new(
        "PRAG0302",
        "Property not found on source type",
        "Property '{0}' not found on source type '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>PRAG0334: the path crosses a boundary, so the navigation was never generated.</summary>
    /// <remarks>
    ///     Replaces PRAG0302 for this case rather than joining it. Both say the property is absent and
    ///     only one says why — and "not found on source type" sends the reader hunting for a typo in a
    ///     name that is spelled correctly, for a relation that is declared, in a codebase where the
    ///     same relation on two entities of the same boundary works.
    /// </remarks>
    public static readonly DiagnosticDescriptor PropertyCrossesABoundary = new(
        "PRAG0334",
        "This path crosses a boundary",
        "'{0}' would reach '{1}' through a relation that crosses from '{2}' to '{3}'. No navigation is "
        + "generated for it: the two entities live in different DbContexts, so there is nothing for EF "
        + "to include. Declare [ReadAccess<{1}>] on '{2}' to read across — the navigation is then "
        + "generated, read-only, and this path resolves — or reach the other side through its own "
        + "operations, or through [RemoteBoundary] when it lives in another host.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "A boundary is the unit a DbContext covers. A relation across one is a fact about the domain, "
        + "not a join the database can perform.");

    /// <summary>
    ///     PRAG0338: the mapping attribute names a type argument this generator cannot resolve.
    /// </summary>
    public static readonly DiagnosticDescriptor UnusableGenericArgument = new(
        "PRAG0338",
        "Mapping attribute names an open type argument",
        "'{0}' carries [{1}] whose type argument is not a concrete type — an open type parameter has "
        + "no properties to map. No mapper is generated. Close the type argument, or move the "
        + "attribute to a closed derived type.",
        "Pragmatic.Mapping",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor NoMatchingSourceProperty = new(
        "PRAG0303",
        "No matching source property",
        "Property '{0}' has no matching source property on '{1}'",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor IncompatibleTypes = new(
        "PRAG0304",
        "Incompatible types",
        "Cannot map from '{0}' to '{1}': incompatible types",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ConverterMustImplementInterface = new(
        "PRAG0305",
        "Converter must implement IValueConverter",
        "Converter '{0}' must implement IValueConverter<{1}, {2}>",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ConverterMustHaveParameterlessConstructor = new(
        "PRAG0306",
        "Converter must have parameterless constructor",
        "Converter '{0}' must have a public parameterless constructor",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor RequiredPropertyNotMapped = new(
        "PRAG0307",
        "Required property not mapped",
        "Target property '{0}' is required but not mapped in [MapTo<{1}>]",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // PRAG0308 was InitOnlySkippedInApplyTo - removed; the id is not reused.

    public static readonly DiagnosticDescriptor NestedTypeMissingMapFrom = new(
        "PRAG0309",
        "Nested type missing [MapFrom]",
        "Nested type '{0}' must have [MapFrom<{1}>] for mapping",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ═══════════════════════════════════════════════════════════════════════════
    // PRAG0310-0319: Projection Diagnostics
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor ProjectionRequiresMapFrom = new(
        "PRAG0310",
        "[GenerateProjection] requires [MapFrom]",
        "[GenerateProjection] requires [MapFrom<T>] on type '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    // PRAG0311 was ProjectionUntranslatableMethod - removed (unreachable: projection content is
    // generator-owned; untranslatable members are excluded by SqlTranslatableAnalyzer, and the
    // specific cases are covered by PRAG0320/PRAG0321/PRAG0326)

    // PRAG0312 was NestedProjectionMissingAttribute - removed (obsolete: nested-DTO inlining no
    // longer requires [GenerateProjection] on the nested type; complex-mapping drops are PRAG0326)

    public static readonly DiagnosticDescriptor CircularReferenceDetected = new(
        "PRAG0313",
        "Circular reference detected",
        "Circular reference detected for '{0}', using instance tracking",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor ConflictingAttributes = new(
        "PRAG0314",
        "Conflicting attributes",
        "Property '{0}' has conflicting attributes [MapIgnore] and [MapProperty]",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NestedDtoMismatch = new(
        "PRAG0315",
        "Nested DTO property mismatch",
        "Nested DTO '{0}' property '{1}' does not match source navigation property type",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NoSuitableConstructor = new(
        "PRAG0316",
        "No suitable constructor found",
        "No suitable constructor found for '{0}' to map init-only properties",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NullableWithoutDefault = new(
        "PRAG0317",
        "Nullable to non-nullable without Default",
        "Cannot map nullable '{0}' to non-nullable '{1}' without specifying Default",
        Category,
        DiagnosticSeverity.Error,
        true);

    // PRAG0318 was PrivateSetterSkipped - removed; the id is not reused.

    public static readonly DiagnosticDescriptor CustomizeMappingIgnoredInProjection = new(
        "PRAG0319",
        "CustomizeMapping ignored in Projection",
        "CustomizeMapping is defined but ignored in Projection for '{0}'",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // ═══════════════════════════════════════════════════════════════════════════
    // PRAG0320-0324: Additional Diagnostics
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor ConverterNotSupportedInProjection = new(
        "PRAG0320",
        "[MapConverter] not supported in Projection",
        "[MapConverter] on property '{0}' is not supported in Projection, property excluded",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor FormatNotSupportedInProjection = new(
        "PRAG0321",
        "Format not supported in Projection",
        "Format string on property '{0}' not translatable to SQL, property excluded from Projection",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor ComplexDictionaryNotSupported = new(
        "PRAG0322",
        "Complex dictionary not supported",
        "Dictionary with complex values on property '{0}' not supported, use [MapIgnore]",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor AmbiguousMapping = new(
        "PRAG0323",
        "Ambiguous mapping",
        "Property '{0}' matches both direct property and convention, use [MapProperty] to be explicit",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor IdPropertyExcluded = new(
        "PRAG0324",
        "ID property excluded from ToEntity",
        "ID property '{0}' excluded from ToEntity() by default, use [MapProperty] to include",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG0325: Source entity has properties not mapped to DTO.
    ///     Default severity: Hidden (configurable to Warning via .editorconfig).
    /// </summary>
    public static readonly DiagnosticDescriptor UnmappedSourceProperty = new(
        "PRAG0325",
        "Source property not mapped to DTO",
        "Source property '{0}.{1}' is not mapped to '{2}' — its data is silently dropped. Add a matching DTO property or map it explicitly with [MapProperty]",
        Category,
        DiagnosticSeverity.Hidden,
        true);

    /// <summary>
    ///     PRAG0326: a nested DTO inlined into a projection has members the inline
    ///     initializer cannot build — a converter, a format string, a path that does not resolve,
    ///     or a source that is neither a column, a nested DTO nor a list of them — so those members
    ///     keep the DTO's default. The standalone NestedDto.Projection honors converters and
    ///     formats; the inlined one does not.
    /// </summary>
    public static readonly DiagnosticDescriptor NestedProjectionMappingDropped = new(
        "PRAG0326",
        "Nested projection drops complex mapping",
        "Nested DTO on property '{0}' of '{1}' has members the projection cannot inline (a converter, a format string, a path that does not resolve, or a source that is neither a column nor a nested DTO); those members keep the DTO's default. Project the nested DTO via its own .Projection or map those members another way.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG0327: nested projection inlining was truncated by the depth cap. Raise [GenerateProjection(MaxDepth = ...)] or project the nested DTO explicitly.
    /// </summary>
    public static readonly DiagnosticDescriptor ProjectionDepthExceeded = new(
        "PRAG0327",
        "Nested projection truncated by MaxDepth",
        "Nested projection on property '{0}' of '{1}' exceeds MaxDepth ({2}); deeper members are omitted. Increase [GenerateProjection(MaxDepth = ...)] or project the nested DTO via its own .Projection",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG0328: enum→enum mapping is by member NAME, validated at compile time — a source member
    ///     with no same-named target member cannot be mapped.
    /// </summary>
    public static readonly DiagnosticDescriptor EnumMemberMissing = new(
        "PRAG0328",
        "Enum member missing on target enum",
        "Cannot map enum property '{0}': source member '{1}' has no matching member on target enum '{2}'. Add the member or map explicitly with a converter",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG0329: [MapCondition] predicate missing or wrong shape (must be a static bool method on
    ///     the DTO taking the source type).
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionMethodInvalid = new(
        "PRAG0329",
        "[MapCondition] predicate missing or invalid",
        "Property '{0}': [MapCondition] method '{1}' must be a static bool method on '{2}' taking the source type",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG0330: [MapDerived&lt;TSource, TDto&gt;] pair violating the contract (derived source must
    ///     derive the [MapFrom] source; derived DTO must derive the base DTO).
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidDerivedMapping = new(
        "PRAG0330",
        "Invalid [MapDerived] pair",
        "[MapDerived] on '{0}': '{1}' must pair a source deriving the [MapFrom] source with a DTO deriving '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG0331: [MapDerived] dispatch is runtime-only — EF projections keep the base shape.
    /// </summary>
    public static readonly DiagnosticDescriptor DerivedMappingIgnoredInProjection = new(
        "PRAG0331",
        "[MapDerived] ignored in Projection",
        "[MapDerived] on '{0}' is not honored by the EF projection (base shape only); query derived DTOs explicitly",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG0332: [MapCondition] gates only the runtime path (FromEntity); the SQL projection maps
    ///     the property unconditionally — the divergence must not be silent.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionIgnoredInProjection = new(
        "PRAG0332",
        "[MapCondition] ignored in Projection",
        "[MapCondition] on property '{0}' of '{1}' applies to FromEntity only; the projection maps it unconditionally",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG0340: a mapped property the projection cannot carry, and therefore leaves at its default.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The projection keeps what it can translate to SQL and drops the rest. Dropping was
    ///         silent, and silence is the worst outcome available here: <c>FromEntity</c> fills the
    ///         property, the projection does not, and the caller of a query-backed read receives the
    ///         DTO&apos;s own initialiser — a plausible value in the shape of the right one. Found on a
    ///         <c>[ValueObject]</c>, whose columns held the right numbers while every projected read
    ///         answered with zeroes.
    ///     </para>
    ///     <para>
    ///         Info rather than Warning: the omission is sometimes the only thing the generator can
    ///         do (a converter, a format string), and the point is that the author knows which
    ///         properties survive a query and which need <c>FromEntity</c>.
    ///     </para>
    /// </remarks>
    // ⚠️ PRAG0340, not 0334: that ID is PropertyCrossesABoundary, in this same file. No automated
    // check detects an ID collision.
    public static readonly DiagnosticDescriptor PropertyDroppedFromProjection = new(
        "PRAG0340",
        "Property omitted from Projection",
        "Property '{0}' of '{1}' is mapped but cannot be translated to SQL, so the projection leaves "
        + "it at its default; reads that go through the projection will not carry it",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG0336: the write path maps to an entity property that has no setter.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A computed property is not a target. Emitting the assignment anyway was <c>CS0200</c>
    ///         inside a generated file — the failure this whole diagnostic range exists to prevent —
    ///         and it only took a bidirectional DTO exposing a value the entity works out, which is a
    ///         normal shape rather than a mistake.
    ///     </para>
    ///     <para>
    ///         So the property is skipped, and skipping in silence would be the other bad answer: the
    ///         author would be reading a shape that says it writes something it does not.
    ///         <c>[MapIgnore(MappingDirection.ToEntity)]</c> is the way to say it, and the message
    ///         names it.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor TargetPropertyIsReadOnly = new(
        "PRAG0336",
        "Mapped property cannot be written: the entity computes it",
        "'{0}.{1}' maps to '{2}.{1}', which has no setter, so the write path leaves it alone. Mark it "
        + "[MapIgnore(MappingDirection.ToEntity)] to say so — it stays in the response either way.",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG0335: <c>[LinkIds]</c> on a DTO that has no tracked write.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Linking a row named only by its key means attaching it to the change tracker as
    ///         <c>Unchanged</c>, and that needs a <c>DbContext</c>. <c>ToEntity()</c> has none — it
    ///         builds an object, it does not write to a database — so on a DTO that is only ever
    ///         built, the declaration can never take effect.
    ///     </para>
    ///     <para>
    ///         Reported rather than ignored, because ignoring it does the opposite of what the line
    ///         says: the author declared which rows the parent should point at, and nothing would
    ///         point anywhere.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor LinkIdsNeedsTheTrackedForm = new(
        "PRAG0335",
        "[LinkIds] is written only through the tracked form",
        "'{0}.{1}' declares [LinkIds(\"{2}\")], which attaches rows to the change tracker and so needs "
        + "a DbContext. Write this DTO with ApplyTo(entity, context) — ToEntity() builds an object and "
        + "cannot link rows, so the declaration has no effect there.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG0333: a collection of DTOs whose elements cannot be matched to the existing children.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Updating a collection means deciding, for each incoming element, whether it is one of the
    ///         children already there. That needs a key, and the key is looked for where a developer has
    ///         already put one: the element DTO's own <c>Id</c>, then the child entity's
    ///         <c>[LogicKey]</c>. With neither, every element looks new — the existing children would be
    ///         removed and rebuilt, losing their identity and their audit trail.
    ///     </para>
    ///     <para>
    ///         An error rather than a quiet fall back to <c>Replace</c>, because the fall back is exactly
    ///         the data loss the strategy exists to prevent, and it would happen on the first update in
    ///         production rather than at build time. Say <c>[CollectionStrategy(Replace)]</c> if wiping
    ///         and rebuilding really is what this collection means.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CollectionElementsCannotBeMatched = new(
        "PRAG0333",
        "This collection's elements cannot be matched",
        "'{0}' on '{1}' is updated with the {2} strategy, which matches incoming elements against the "
        + "existing children — but {3}. Give the element DTO an Id, or the child entity a [LogicKey] the "
        + "DTO also carries, or declare [CollectionStrategy(CollectionStrategy.Replace)] if rebuilding "
        + "the collection from scratch is intended.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "A keyed collection update needs something to match elements by on both sides.");

    /// <summary>
    ///     PRAG0341 — the source can be absent and the DTO says it cannot, so the generated mapping puts
    ///     a default there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For a simple or enum target the substitution is a feature: the mapping synthesises
    ///         <c>default</c> instead of refusing, which is why <see cref="NullableWithoutDefault" />
    ///         (PRAG0317, an error) deliberately excludes those types. What was missing is that nobody was
    ///         told. A <c>DateTimeOffset</c> declared over a nullable <c>UpdatedAt</c> puts
    ///         <c>0001-01-01T00:00:00+00:00</c> on the wire for every row never updated — a date that
    ///         reads like data.
    ///     </para>
    ///     <para>
    ///         Info, not a warning: the substitution is declared behaviour and every application in this
    ///         repository would light up. It names both types and the direction so the author can choose
    ///         — declare the DTO property nullable, or say <c>[MapProperty(Default = …)]</c> and mean it.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor NullableSourceDefaulted = new(
        "PRAG0341",
        "Nullable source on a non-nullable property",
        "'{0}' maps a nullable '{1}' onto a non-nullable '{2}': where the source is null the generated "
        + "mapping substitutes default({2}). Declare the property as '{2}?', or give it an explicit "
        + "[MapProperty(Default = ...)], if that value would be mistaken for data.",
        Category,
        DiagnosticSeverity.Info,
        true);
}
