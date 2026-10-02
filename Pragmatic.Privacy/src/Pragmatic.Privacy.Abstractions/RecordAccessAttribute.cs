namespace Pragmatic.Privacy;

/// <summary>
///     Records every successful execution of this operation in the audit trail, because it reads
///     personal data.
/// </summary>
/// <remarks>
///     <para>
///         <b>Writes are recorded on every path; reads are recorded nowhere.</b> The audit trail is
///         written by an interceptor over <c>SaveChanges</c>, so it sees a change and never a query. For
///         most applications that is the right amount: reads outnumber writes by orders of magnitude, and
///         a trail that records all of them is a trail nobody can search when it matters.
///     </para>
///     <para>
///         For some operations it is not enough. "Who looked at this person's record" is a question a
///         health, legal or HR system has to be able to answer, and it cannot be answered afterwards —
///         the evidence either was written at the time or does not exist. This attribute is how an
///         operation says it is one of those, one operation at a time rather than as a global setting
///         nobody can afford to leave on.
///     </para>
///     <para>
///         What is recorded is the operation, the actor and the time — not the rows returned. A list
///         query answering with fifty people would otherwise write fifty entries per call, and the trail
///         would hold a second copy of the very data it exists to protect.
///     </para>
///     <para>
///         The Article 30 register reports which reads carry this and which do not, so leaving it off is
///         a visible decision rather than an omission.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RecordAccessAttribute : Attribute;
