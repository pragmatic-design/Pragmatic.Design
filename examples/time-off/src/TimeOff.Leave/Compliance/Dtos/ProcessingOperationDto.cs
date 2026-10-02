namespace TimeOff.Leave.Dtos;

/// <summary>One operation that processes personal data: what it touches, whether it reads or writes, and where it is reached.</summary>
[MapFrom<ProcessingOperation>]
public sealed partial record ProcessingOperationDto(
    string OperationType,
    string Access,
    string EntityType,
    IReadOnlyList<string> Categories,
    string? Route,
    bool Recorded,
    string? Purpose);
