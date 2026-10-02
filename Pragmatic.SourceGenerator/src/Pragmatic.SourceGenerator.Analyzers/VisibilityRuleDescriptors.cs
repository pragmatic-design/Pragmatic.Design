using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Descriptors for the declared visibility rules and for lifting filters, <c>PRAG0717</c>–<c>PRAG0721</c>.
/// </summary>
/// <remarks>
///     In the analyzer rather than in the generator's own diagnostics because every one of these is a
///     question about symbols — what a type derives from, whether it can be constructed, what
///     attributes sit beside it — which an analyzer holds and a transform would have to carry across
///     in a model. The generator's <c>QueryPipelineDiagnostics</c> already records what happens when
///     descriptors are declared where nothing reports them.
/// </remarks>
internal static class VisibilityRuleDescriptors
{
    private const string Category = "Pragmatic.Persistence";

    /// <summary>PRAG0717: the named rule filters a different entity.</summary>
    public static readonly DiagnosticDescriptor WrongEntity = new(
        "PRAG0717",
        "Visibility rule is for another entity",
        "'{0}' declares [VisibleWhen<{1}>], but {1} is not a VisibilityRule<{0}>. A rule filters the "
        + "entity it is typed for, so this one would never apply to {0}.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "The rule is registered as IQueryFilter for its own entity type. Naming it here registers a "
        + "filter for a different entity and leaves this one unfiltered.");

    /// <summary>PRAG0718: the rule cannot be built while the model is.</summary>
    /// <remarks>
    ///     <para>
    ///         A declared rule becomes an EF Core named global query filter, which is what makes it
    ///         reach an <c>Include</c> and a projected subquery rather than only the root of a query.
    ///         Its predicate is therefore asked for once, while the model is built, from
    ///         <c>new TRule().ToExpression()</c>.
    ///     </para>
    ///     <para>
    ///         That is the whole of the constraint: no constructor arguments, and no generic
    ///         parameters left open or closed at the use site. It also rules out a rule that wants the
    ///         current user, on purpose — the model is cached per context type, so a scoped dependency
    ///         captured here would be baked in and served to everyone afterwards. A predicate that
    ///         depends on the caller belongs to the ownership and scope filters, which compose
    ///         additively and are evaluated per request.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor NotConstructible = new(
        "PRAG0718",
        "Visibility rule cannot be constructed while the model is built",
        "{0} is declared as a visibility rule but is abstract, generic, or has no public parameterless "
        + "constructor. Its predicate is read once when the EF model is built, so it has to be "
        + "constructible with no arguments.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "A rule whose predicate depends on the caller cannot be a model-level filter at all: the model "
        + "is cached, so what it captures is served to every later request.");

    /// <summary>PRAG0719: lifting query filters without declaring the privilege that allows it.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>[WithoutFilter&lt;T&gt;]</c> turns filters off for the operation it sits on. Doing
    ///         that is a privilege, and an operation that does it without declaring one answers
    ///         everybody alike.
    ///     </para>
    ///     <para>
    ///         The same shape has bitten three times: a composite action answering 204 to a caller
    ///         with no permission, a boundary facade skipping authorization, a filter mode that lifted
    ///         one rule of the two it promised. A permission that is never asked for leaves no trace in
    ///         any log.
    ///     </para>
    ///     <para>
    ///         It asks for a permission, not for the right one — which is all a compiler can check.
    ///         What it buys is that the privilege is named, and greppable.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor WithoutFilterNeedsPermission = new(
        "PRAG0719",
        "Lifting a query filter needs a permission",
        "'{0}' declares [WithoutFilter<{1}>] and no [RequirePermission]. Reading past a filter is a "
        + "privilege — declare the permission that grants it, or drop the attribute.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "Without a permission the operation lifts the filter for every caller, and nothing records "
        + "that it did.");

    /// <summary>PRAG0720: disabling a declared rule by type, which reaches nothing.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>Disable&lt;TFilter&gt;()</c> takes a filter out of <c>DefaultQueryFilterProvider</c>.
    ///         A declared rule is not in it — it is installed on the EF model — so the call compiles,
    ///         returns a scope, and changes nothing about what the query returns.
    ///     </para>
    ///     <para>
    ///         This is the shape that cost the most to find: the tenant rule was enforced twice and
    ///         <c>FilterMode.Background</c> lifted one of the two, so a background job asking to read
    ///         across tenants still read zero rows and said nothing. A filter that is asked to step
    ///         aside and does not has to be a compile error, not a measurement.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DisableByTypeDoesNothing = new(
        "PRAG0720",
        "Disabling a visibility rule by type has no effect",
        "{0} is a declared visibility rule, so it is enforced by EF Core rather than by the filter "
        + "provider that Disable<T>() reaches. Call DisableVisibilityRule<{0}>() instead.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "The call compiles and does nothing: the query returns exactly the rows it would have without "
        + "it.");

    /// <summary>PRAG0721: writing the property a rule keys on, without being able to reach the row.</summary>
    /// <remarks>
    ///     <para>
    ///         A declared rule is an EF Core global query filter, so a row that stops satisfying it is
    ///         invisible to <b>every</b> query of the entity — an update included, because an update
    ///         loads through the same filtered query. The operation that sets the property back cannot
    ///         load the row it exists to fix, and the state the rule keys on becomes a one-way door.
    ///     </para>
    ///     <para>
    ///         Narrow on purpose. It fires only when the rule's predicate is a plain member access, so
    ///         the property is known, <b>and</b> the operation writes that property. A blunter version
    ///         — every write of an entity that carries a rule — would flag the ones that only ever
    ///         load rows still satisfying it, which is most of them, and a diagnostic that is usually
    ///         wrong gets suppressed rather than read.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>What it does not see.</b> "Writes" means the mutation declares the property, so the
    ///         caller can set it — the reactivate shape. A mutation that assigns it imperatively inside
    ///         <c>ApplyToEntity</c> is invisible here, and that is the right answer for the common one
    ///         of those: a retire sets the flag to the value that <em>fails</em> the rule, loads a row
    ///         that still satisfies it, and needs nothing. It is the wrong answer for an imperative
    ///         reactivate, which this will not catch.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor WriteNeedsWithoutFilter = new(
        "PRAG0721",
        "Writing the property a visibility rule keys on",
        "'{0}' writes {1}, which '{2}' keys on, so a row that stops satisfying the rule cannot be "
        + "loaded to change it back. Add [WithoutFilter<{2}>] beside the permission that allows it.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "The rule is enforced by EF Core on every query of the entity, so the operation that manages "
        + "these rows has to say it is allowed past it.");
}
