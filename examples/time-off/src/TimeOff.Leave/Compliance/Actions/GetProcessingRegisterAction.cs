namespace TimeOff.Leave.Compliance.Actions;

/// <summary>
///     The register of processing activities, as the application stands now.
/// </summary>
/// <remarks>
///     Built from the code on every call rather than kept as a document: a register maintained by hand
///     describes the system as somebody remembered it, and drifts from the first column added afterwards.
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.ProcessingRegister.Read)]
[Endpoint(HttpVerb.Get, "api/compliance/processing-register")]
public partial class GetProcessingRegisterAction : DomainAction<ProcessingRegisterDto>
{
    private IProcessingRegisterBuilder _register = null!;

    public override async Task<Result<ProcessingRegisterDto, IError>> Execute(CancellationToken ct = default)
    {
        var register = await _register.BuildAsync(ct).ConfigureAwait(false);
        return ProcessingRegisterDto.FromEntity(register);
    }
}
