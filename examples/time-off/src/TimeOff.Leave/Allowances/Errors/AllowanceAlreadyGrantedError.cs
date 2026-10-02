namespace TimeOff.Leave.Errors;

/// <summary>
///     A grant to a whole team, refused because some of its members already hold an allowance of that
///     kind for that year. Nobody was granted one: it is all or nothing.
/// </summary>
/// <remarks>
///     The words are in <c>translations/*.json</c>, under <c>error.allowance.already.granted</c>, and the
///     employees go into them through <see cref="Parameters" />: <c>{employees}</c>.
/// </remarks>
public sealed partial record AllowanceAlreadyGrantedError : Error
{
    public override string Code => "ALLOWANCE_ALREADY_GRANTED";
    public override int StatusCode => 409;

    /// <summary>The members who already hold one, by their employee number.</summary>
    public IReadOnlyList<string> EmployeeNumbers { get; init; } = [];

    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["employees"] = string.Join(", ", EmployeeNumbers)
    };
}
