using System.Text;
using Pragmatic.Cryptography;

namespace TimeOff.Leave.LeaveRequests.Actions;

/// <summary>
///     Why an employee asked for this leave — decrypted, or the reason it cannot be.
/// </summary>
/// <remarks>
///     <para>
///         <b>An action and not a query, because the read is not declarable.</b> A
///         <c>[Query&lt;,&gt;]</c> is a projection: it runs in SQL and returns columns. This one has to
///         resolve a key, decrypt, and answer with one of three outcomes — the text, "there is no key
///         for this value", and "the key was destroyed". None of that is a column, and a projection
///         that returned an empty string for the last two would say the employee gave no reason.
///     </para>
///     <para>
///         ⚠️ <c>KeyDestroyed</c> is <b>not</b> a failure. It is what an erasure looks like from the
///         read side, and it is a distinct outcome from <c>AuthenticationFailed</c> precisely so the
///         two are never confused: the first is the system working, the second is a security signal.
///         So this answers 200 with the outcome named, rather than 404 or 500.
///     </para>
///     <para>
///         <c>[RecordAccess]</c>: reading special category data about a person is recorded in the audit
///         trail, with who read it and when — and not what they read.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.LeaveRequest.Read)]
[LoadEntity<LeaveRequest>(nameof(Id))]
[RecordAccess]
[Endpoint(HttpVerb.Get, "api/leave-requests/{id}/reason")]
public partial class ReadTheReasonAction : DomainAction<LeaveRequestReasonDto, NotFoundError>
{
    private ISubjectDataProtector _protector = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<LeaveRequestReasonDto, IError>> Execute(CancellationToken ct = default)
    {
        // Nothing was written: the employee gave no reason. Distinct from every other answer, and the
        // only one of them that is about the employee rather than about the key.
        if (_leaveRequest.Reason.IsEmpty)
            return new LeaveRequestReasonDto { Outcome = nameof(LeaveRequestReason.NotGiven) };

        var read = await _protector.TryReadAsync(_leaveRequest.Reason.Packed, ct: ct).ConfigureAwait(false);

        return read.Outcome switch
        {
            DecryptOutcome.Success => new LeaveRequestReasonDto
            {
                Outcome = nameof(LeaveRequestReason.Given),
                Reason = Encoding.UTF8.GetString(read.Plain),
            },
            DecryptOutcome.KeyDestroyed => new LeaveRequestReasonDto
            {
                Outcome = nameof(LeaveRequestReason.Erased),
            },
            // Everything else is the security signal: the ciphertext does not authenticate, or names a
            // key this deployment never issued. Answered as not found rather than described — what
            // exactly failed is for the log, not for the caller.
            _ => new NotFoundError { EntityType = nameof(LeaveRequest), EntityId = Id.ToString() },
        };
    }
}
