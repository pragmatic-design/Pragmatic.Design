namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks a parameterless instance method that returns <see cref="bool"/> and is <b>at least
///     internal</b> as an aggregate invariant. The generated invoker calls it after the operation's body
///     and before persist; a method returning <see langword="false"/> rejects that operation with an
///     <c>InvariantViolationError</c> (HTTP 422).
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The shape is load-bearing, and "at least internal" is the part nobody expects.</b> The
///         invoker is a generated class beside the entity, so it can only call a method the entity lets
///         its own assembly call: a <c>private</c> rule is a <c>CS0122</c> if generated and is therefore
///         skipped. Five conditions have to hold — parameterless, instance, returning <c>bool</c>, at
///         least <c>internal</c>, with a name no other invariant of the entity uses — and failing any of
///         them is <c>PRAG0463</c>, an error that names which condition failed. Silence would leave the
///         rule reading as enforced while it is checked on no path at all, with the operation it was
///         meant to refuse answering 201 — and <c>private</c> is exactly how a rule nobody calls from
///         outside tends to be written.
///     </para>
///     <para>
///         <b>Which operations it is a guarantee about.</b> A <c>[Mutation]</c> checks every rule of the
///         aggregate it writes. A <c>[DomainAction]</c> that <c>[LoadEntity]</c>-ed the aggregate checks
///         the rules it <b>can answer</b>: those whose body reads no navigation outside the action's
///         <c>Include</c> list.
///     </para>
///     <para>
///         ⚠️ That qualification is not timidity, it is a measurement. An action includes what its own
///         body needs — voiding an invoice includes its lines and not its payments — so a rule over the
///         payments evaluated there would compare a real amount against an empty collection and refuse a
///         write that is correct. Skipping it leaves that rule unchecked on this path; checking it would
///         be a wrong answer, with a 422 attached.
///     </para>
///     <para>
///         ⚠️ <b>Still not every write.</b> A repository <c>Add</c>/<c>Remove</c> from a job or a seeding
///         step runs none of these methods, and neither does an action that changes the aggregate without
///         loading it through <c>[LoadEntity]</c>. "Invalid state never reaches the database" is what this
///         summary once claimed, and it is still not true of every path. A rule that must hold
///         everywhere belongs in the entity method that writes it, which every caller goes through.
///     </para>
///     <para>
///         Invariants express aggregate-level rules that must always hold (e.g. "an order's total equals
///         the sum of its lines"). They differ from validation: validation guards input shape at the
///         edge, invariants guard the consistency of the aggregate itself. Keep them pure and
///         side-effect-free.
///     </para>
///     <para>
///         A <c>[PartOf]</c> child may declare its own, and the <b>aggregate's</b> invoker checks them
///         after it has merged the child: that is the only write path a child has, so it is the only
///         place its rules can be checked.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class InvariantAttribute : Attribute
{
    /// <summary>Creates an invariant with the default message.</summary>
    public InvariantAttribute() { }

    /// <summary>Creates an invariant with an explanatory message used in the error when it is violated.</summary>
    /// <param name="message">Human-readable explanation of the rule.</param>
    public InvariantAttribute(string message) => Message = message;

    /// <summary>Optional human-readable explanation surfaced in <c>InvariantViolationError</c>.</summary>
    public string? Message { get; }

    /// <summary>
    ///     The translation key the refusal reports, so the rule can be explained in the caller's
    ///     language. <see cref="Message" /> stays the answer where the key has no translation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is an error key like any other in the stack, so it is the <b>base</b>: the localizer
    ///         reads <c>{key}.title</c> for the sentence and <c>{key}.detail</c> for the explanation, and
    ///         the translation files carry those two.
    ///     </para>
    ///     <para>
    ///         ⚠️ Written as a string, and not as a constant of the generated keys class the way a
    ///         validation rule's <c>MessageKey</c> is. A base has no constant to name: in that class
    ///         <c>.title</c> and <c>.detail</c> are the members and the base is the nested type holding
    ///         them, and a translation file that carried both a bare key and a suffixed one is refused
    ///         (<c>PRAG1805</c>) precisely so the two never collide.
    ///     </para>
    ///     <para>
    ///         Without it the refusal reports <c>error.invariant.violation</c>, which every invariant in
    ///         the application shares: translating that means giving up which rule refused. It made an
    ///         <c>[Invariant]</c>'s sentence the only text in the stack with no way to be localized.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     [Invariant("The VAT rate must be one of 0, 4, 5, 10 or 22 per cent",
    ///         MessageKey = "error.unknown_vat_rate")]
    ///     internal bool ChargesARateInUse() => …;
    ///
    ///     // it-IT.json
    ///     // "error.unknown_vat_rate.title":  "Aliquota IVA non prevista",
    ///     // "error.unknown_vat_rate.detail": "Le aliquote ammesse sono 0, 4, 5, 10 e 22 per cento."
    ///     </code>
    /// </example>
    public string? MessageKey { get; set; }
}
