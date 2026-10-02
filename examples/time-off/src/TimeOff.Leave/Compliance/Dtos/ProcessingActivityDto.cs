namespace TimeOff.Leave.Dtos;

/// <summary>One kind of record holding personal data: its categories, how each field is erased, and why it is held.</summary>
[MapFrom<ProcessingActivity>]
public sealed partial record ProcessingActivityDto(
    string EntityType,
    IReadOnlyList<string> Categories,
    bool IsDataSubject,
    IReadOnlyDictionary<string, string> Erasure,
    IReadOnlyList<RetainedItem> Retained,
    string? Purpose);
