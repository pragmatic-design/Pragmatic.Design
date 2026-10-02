namespace Casework.Verify.Verifications.Mutations;

/// <summary>
///     Writes down a verification somebody asked for.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[Endpoint]</c>: nobody asks for a verification over HTTP — it arrives on the bus. That
///         makes this the boundary's <b>internal</b> surface, which is exactly what the generated
///         <c>IVerifyInternalActions</c> exists for, and it is how a message handler writes: the
///         operation owns the transaction, so a handler never holds a unit of work of its own.
///     </para>
///     <para>
///         It needs no <c>ApplyAsync</c>: a create mutation's inputs are mapped onto the entity by name,
///         and <c>CaseId</c> and <c>Kind</c> are all this row is.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
public partial class RecordVerificationRequestMutation : Mutation<Verification>
{
    /// <summary>The case this is about, in Intake's database. An identifier, not a relation.</summary>
    public required Guid CaseId { get; init; }

    [Required]
    [MaxLength(60)]
    public required string Kind { get; init; }

    /// <summary>By when the asking service expects an answer, as the request said.</summary>
    /// <remarks>
    ///     Taken from the message and not computed here: the window is Intake's expectation, so this
    ///     service records it rather than having an opinion of its own — two services each with their own
    ///     idea of "ten days" is a disagreement nobody can see.
    /// </remarks>
    public required DateTimeOffset Deadline { get; init; }
}
