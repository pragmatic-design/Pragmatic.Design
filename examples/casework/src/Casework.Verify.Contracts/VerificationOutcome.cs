namespace Casework.Verify.Events;

/// <summary>
///     What a verification found.
/// </summary>
/// <remarks>
///     <para>
///         It lives in the <b>contract</b> and not in Verify's module, because it is the thing the two
///         services say to each other: Intake has to be able to read an outcome without referencing
///         Verify's domain. Verify uses it from here too — one enum, one meaning, and no mapping between
///         an internal and an external spelling of the same two words.
///     </para>
///     <para>
///         ⚠️ Not a <c>bool</c>. <c>true</c> in a payload is a fact nobody can name, and a consumer
///         written against it cannot tell "passed" from "answered". It has no "pending" member either:
///         whether an answer has arrived is the verification's <b>status</b>, which is Verify's own
///         business and does not cross.
///     </para>
///     <para>
///         ⚠️ No <c>[FastEnum]</c> here: that generates converters and parsing helpers for a module, and
///         this assembly deliberately takes only Abstractions and Events. The enum travels as its
///         <b>name</b> in JSON because the hosts serialize enums as strings, which is also what keeps a
///         reordered member from changing the meaning of a message already on the wire.
///     </para>
/// </remarks>
public enum VerificationOutcome
{
    /// <summary>What was asked for checks out.</summary>
    Passed,

    /// <summary>It does not, and the note says why.</summary>
    Failed,

    /// <summary>
    ///     It could not be checked at all — the document was unreadable, the registry was down, the
    ///     person could not be reached.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not a third way of deciding a case: it is an answer the process <b>cannot act on</b>, and
    ///     it is what makes the saga's rejection path real. <c>Passed</c> and <c>Failed</c>
    ///     both decide the case; this one says the question is still open, so the process gives up,
    ///     undoes what it asked for, and the case goes back to where an operator can ask again.
    ///     Appended rather than inserted: the enum travels as its <b>name</b>, but a reader that stored
    ///     the number would not thank us either.
    /// </remarks>
    Inconclusive
}
