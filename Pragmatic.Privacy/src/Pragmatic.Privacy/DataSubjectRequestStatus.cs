namespace Pragmatic.Privacy;

/// <summary>
///     Where a data subject request has got to.
/// </summary>
public enum DataSubjectRequestStatus
{
    /// <summary>Recorded, nothing done yet. The clock starts here.</summary>
    Received = 0,

    /// <summary>
    ///     The requester has been shown to be who they claim.
    /// </summary>
    /// <remarks>
    ///     Not a formality. Acting on an erasure request for the wrong person because someone mistyped
    ///     an address is an incident, not an act of compliance — and it is not undoable.
    /// </remarks>
    Verified = 1,

    /// <summary>Being carried out.</summary>
    Executing = 2,

    /// <summary>Carried out in full.</summary>
    Completed = 3,

    /// <summary>
    ///     Carried out as far as the law allows, with some data retained.
    /// </summary>
    /// <remarks>
    ///     <b>A complete and legitimate outcome, not a failure.</b> "Erased 12 records, 2 retained under
    ///     a fiscal obligation" is the ordinary case, and modelling it as an error would force a choice
    ///     between lying and failing. What must not be silent is the reason, which travels with it.
    /// </remarks>
    Partial = 4,

    /// <summary>Refused — identity not established, or the request does not apply.</summary>
    Rejected = 5
}
