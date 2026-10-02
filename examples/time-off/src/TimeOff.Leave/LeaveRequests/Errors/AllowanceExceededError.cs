namespace TimeOff.Leave.Errors;

/// <summary>
///     The request takes more than is left of the allowance — what is approved and what is pending both
///     count as taken.
/// </summary>
/// <remarks>
///     The words are in <c>translations/*.json</c>, under <c>error.allowance.exceeded</c>, and the
///     numbers go into them through <see cref="Parameters" />: <c>{requested}</c>, <c>{remaining}</c>.
/// </remarks>
public sealed partial record AllowanceExceededError : Error
{
    public override string Code => "ALLOWANCE_EXCEEDED";
    public override int StatusCode => 422;

    /// <summary>What the request would take, in the kind's unit.</summary>
    public decimal Requested { get; init; }

    /// <summary>What is left, in the kind's unit.</summary>
    public decimal Remaining { get; init; }

    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["requested"] = Requested,
        ["remaining"] = Remaining
    };
}
