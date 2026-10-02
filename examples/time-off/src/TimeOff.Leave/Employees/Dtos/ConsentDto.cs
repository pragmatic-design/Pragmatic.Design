namespace TimeOff.Leave.Dtos;

/// <summary>A consent the employee gave, withdrawn ones included: the record of a withdrawal is theirs too.</summary>
[MapFrom<ConsentRecord>]
public sealed partial record ConsentDto(string Purpose, string NoticeVersion, DateTimeOffset GrantedAt, DateTimeOffset? WithdrawnAt);
