using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Validation.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Validation source generator.
///     ID Range: PRAG0200-0299
/// </summary>
internal static class ValidationDiagnostics
{
    private const string Category = "Pragmatic.Validation";

    // PRAG0200 (type with validation attributes must be partial) is the companion analyzer's, which
    // reports it on the declaration (NotPartialDiagnosticDescriptors); the generator skips the type
    // silently.

    public static readonly DiagnosticDescriptor ValidatorMustImplementInterface = new(
        "PRAG0201",
        "[Validator] class must implement IValidator<T>",
        "Class '{0}' has [Validator] attribute but does not implement IValidator<T>",
        Category, DiagnosticSeverity.Error, true,
        "Classes marked with [Validator] must implement IValidator<T> for some type T.");

    // NOTE: PRAG0202 (PropertyNotPartialWarning) was declared here but never reported — the non-partial
    // case is already covered by PRAG0200 on the type. Removed: do not reuse the ID for anything else.

    public static readonly DiagnosticDescriptor ComparisonPropertyNotFound = new(
        "PRAG0203",
        "Comparison property not found",
        "Property '{0}' referenced by [{1}] on '{2}' was not found",
        Category, DiagnosticSeverity.Error, true,
        "Comparison attributes like [EqualTo], [GreaterThanProperty] require the referenced property to exist on the same type.");

    public static readonly DiagnosticDescriptor ValidateElementsOnNonCollection = new(
        "PRAG0204",
        "[ValidateElements] on non-collection type",
        "Property '{0}' has [ValidateElements] but is not a collection type",
        Category, DiagnosticSeverity.Warning, true,
        "[ValidateElements] should only be used on collection properties (List<T>, T[], IEnumerable<T>, etc.).");

    public static readonly DiagnosticDescriptor ValidateElementsNotValidatable = new(
        "PRAG0205",
        "[ValidateElements] element type doesn't implement ISyncValidator",
        "Element type '{0}' of property '{1}' does not implement ISyncValidator",
        Category, DiagnosticSeverity.Error, true,
        "Collection elements must implement ISyncValidator (have validation attributes and be partial) to use [ValidateElements].");

    /// <summary>
    ///     PRAG0223: <c>[ValidateElements]</c> written bare, where it configures nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generator walks a collection's elements whenever the property is a collection
    ///         <b>and</b> its element type is an <c>ISyncValidator</c> — which is the attribute's own
    ///         precondition, the one <c>PRAG0205</c> enforces. So where the attribute is legal it is
    ///         redundant, and where it would add something it is already refused. The two conditions
    ///         coincide, and always did.
    ///     </para>
    ///     <para>
    ///         ⚠️ An <b>error</b>, and that is the decision. Documenting it as a marker
    ///         would leave a form in the public surface that does nothing, which is what this family of
    ///         diagnostics exists to remove; an author who writes it believes they turned something on.
    ///         <c>StopOnFirstError</c> is the attribute's one reachable setting, so the attribute has
    ///         exactly one legitimate spelling and this says which.
    ///     </para>
    ///     <para>
    ///         ⚠️ Not reported when the element type is not validatable: that is <c>PRAG0205</c>'s
    ///         case, and telling that author their attribute is redundant would be false — nothing
    ///         walks those elements at all.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ValidateElementsConfiguresNothing = new(
        "PRAG0223",
        "[ValidateElements] configures nothing here",
        "Property '{0}' declares [ValidateElements] with no setting: elements of '{1}' are validated "
        + "because the type is an ISyncValidator, not because of this attribute, and the declaring "
        + "type has other validation rules that give it a validator",
        Category, DiagnosticSeverity.Error, true,
        "Write [ValidateElements(StopOnFirstError = true)] if that is what you want — it is the one "
        + "setting that changes the generated loop — or remove the attribute: the elements are "
        + "validated either way, indexed error paths included. "
        + "This is reported only where the declaring type carries other validation rules, or a "
        + "required member, and would therefore be validated without the attribute. On a type whose "
        + "only annotation is this one, the bare form is legal and kept: there it decides something "
        + "real — that a validator is generated at all — and deleting it leaves the elements "
        + "unvalidated.");

    // NOTE: PRAG0206 (ValidatorNoValidatedType) was declared here but never reported — a [Validator]
    // that validates a type without attribute-based validation is a legitimate shape, not a warning.
    // Removed: do not reuse the ID for anything else.

    // [ValidEnum] on something that is not an enum (PRAG0220)
    //
    // The rule compiles to Enum.IsDefined<T>, whose T is constrained to a non-nullable value type, so
    // the attribute on a string produced a CS0453 from inside a generated file: an error about a
    // constraint, in code the author cannot open, naming neither the property nor the attribute that
    // caused it. The check is knowable here, and the rule is skipped rather than written broken.
    public static readonly DiagnosticDescriptor ValidEnumOnNonEnum = new(
        "PRAG0220",
        "[ValidEnum] requires an enum",
        "Property '{0}' is '{1}', which is not an enum: [ValidEnum] checks that a value is a defined member of its enum type, and has nothing to check here",
        Category, DiagnosticSeverity.Error, true,
        "Remove [ValidEnum], or apply it to a property whose type is an enum.");

    // A validatable type nested in a type that is not partial (PRAG0221)
    //
    // The generated file reopens every enclosing type so the nesting survives, and C# allows a second
    // declaration only of a partial type. Dropping the nesting instead would write the validator at
    // namespace level, which produces CS1527 for an accessibility a namespace member cannot have, plus a run of errors on the record members the compiler synthesises — all inside a
    // file the author cannot open, naming members nobody wrote.
    public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        "PRAG0221",
        "A validated type's containers must be partial",
        "'{0}' is validated, so its container '{1}' is reopened in generated code and must be declared partial",
        Category, DiagnosticSeverity.Error, true,
        "Add the partial modifier to the enclosing type, or move the validated type out of it.");

    // An async validator for an operation declared in another assembly (PRAG0215)
    //
    // Whether a mutation or a domain action runs an async validator is decided by the generator of
    // the operation's own assembly, from the [Validator] classes it can see (docs/CONVENTIONS.md, «Decide at compile time»). A
    // validator declared elsewhere is one it cannot see: registered by the host, and never called.
    // With [Validate] on the operation the runtime still resolves it from the container, so that
    // case is not reported.
    public static readonly DiagnosticDescriptor AsyncValidatorOutsideTheOperationsAssembly = new(
        "PRAG0215",
        "An operation's async validator must be declared in the operation's assembly",
        "'{0}' validates '{1}', which is declared in '{2}': whether '{1}' runs an async validator is decided in '{2}', which cannot see this one, so it never runs",
        Category, DiagnosticSeverity.Warning, true,
        "Move the validator into the assembly that declares the operation, or put [Validate] on the operation.");

    // A MessageKey that names a constant the generator cannot read (PRAG0222)
    //
    // A TKeys constant is written by this generator, so while the rule is read it does not exist and the
    // argument has no value; the constants' catalog gives it back. One the catalog does not hold leaves
    // the rule on its default key, and without this nothing would say so — the compiler, running after
    // the generator, accepts the argument if the constant comes from anywhere at all.
    public static readonly DiagnosticDescriptor MessageKeyCannotBeRead = new(
        "PRAG0222",
        "A MessageKey the generator cannot read",
        "'{0}.{1}' names its message with '{2}', which has no value while the validator is generated and is no translation key constant: the rule reports its default key",
        Category, DiagnosticSeverity.Warning, true,
        "Name a constant of the generated translation keys (TKeys.…), or write the key as a string.");

    public static readonly DiagnosticDescriptor IncompatibleComparisonTypes = new(
        "PRAG0209",
        "Incompatible comparison types",
        "Property '{0}' ({1}) and '{2}' ({3}) have incompatible types for comparison",
        Category, DiagnosticSeverity.Warning, true,
        "Comparison attributes require properties of compatible types.");
}
