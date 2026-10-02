namespace TimeOff.Leave.Dtos;

/// <summary>
///     The register of processing activities (GDPR Article 30): who the controller is, what personal data
///     each kind of record holds and how each field is erased, and the operations that process it.
/// </summary>
/// <remarks>
///     Derived from the classification on the entities, so it describes the code as it is. What the code
///     cannot say — why each activity happens — is declared by the company in the host, and
///     <see cref="IsComplete" /> says whether it has been.
/// </remarks>
[MapFrom<ProcessingRegister>]
public sealed partial record ProcessingRegisterDto(
    string ControllerName,
    string ControllerContact,
    DateTimeOffset GeneratedAt,
    bool IsComplete,
    IReadOnlyList<ProcessingActivityDto> Activities,
    // The register's own list is nullable; this one is the never-null view of it.
    [MapProperty(nameof(ProcessingRegister.ProcessingOperations))] IReadOnlyList<ProcessingOperationDto> Operations);
