namespace Casework.Intake.Cases.Mutations;

/// <summary>
///     Asks for a verification of a case: the case's move, and the event the other service consumes.
/// </summary>
/// <remarks>
///     <para>
///         The operation does not publish anything. It calls the entity, the entity raises the event, and
///         the outbox interceptor writes it into <c>__OutboxMessages</c> <b>in the same transaction</b> as
///         the row it is about. An operation that published on
///         the bus itself would announce a change a failed commit never made.
///     </para>
///     <para>
///         The route is <c>POST api/cases/{id}/verifications</c> — a verification is asked for by
///         creating one, not by patching a status: there is no route that writes <c>Status</c>, and
///         <c>TheCasesSurface.NoRouteWritesAStatus</c> is what keeps it that way. The move to
///         <c>InVerification</c> happens because the case was asked to do something, which is the
///         difference between a state machine and a column.
///     </para>
///     <para>
///         Nothing waits for the answer. The response is the case in its new state; whoever verifies it
///         answers when they answer, into Verify's own API, and that is a different process. <c>NothingInIntakeWaitsForTheAnswer</c> asserts it with Verify deaf.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Post, "api/cases/{id}/verifications")]
// Update and not Create: what the POST creates is a verification, and a verification is not a resource
// anybody writes on its own — it is the case moving, and the case is what the caller has to be allowed
// to change.
[RequirePermission(IntakePermissions.Case.Update)]
[ReturnsDto<CaseDto>]
public partial class AskForVerificationMutation : Mutation<Case>
{
    /// <summary>
    ///     What is to be verified.
    /// </summary>
    /// <remarks>
    ///     <c>[MapIgnore]</c> because it is an argument of the move and not a column of the case: an
    ///     update mutation maps its inputs onto the entity by name, and the generator said so rather than
    ///     writing a setter nobody declared (PRAG0414/PRAG0303/PRAG0307). Where it goes is into the
    ///     event.
    /// </remarks>
    [Required]
    [MaxLength(60)]
    [MapIgnore]
    public required string Kind { get; init; }

    /// <summary>When it was asked: the application's clock, never the caller's.</summary>
    [FromClock]
    public DateTimeOffset AskedOn { get; private set; }

    public override Task<Result<Case, IError>> ApplyAsync(Case entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // The move can be refused — a case already in verification, or decided — and the refusal is the
        // state machine's, not an `if` written here.
        var moved = entity.AskForVerification(Kind, AskedOn);

        // `Result<T, IError>.Failure(...)` and not `return moved.Error;`: the implicit conversion from the
        // error is a user-defined one, and C# does not apply those when the source type is an interface.
        // A concrete error type converts on its own — an `IError` in hand needs the factory.
        if (moved.IsFailure)
            return Task.FromResult(Result<Case, IError>.Failure(moved.Error));

        return Task.FromResult<Result<Case, IError>>(entity);
    }
}
