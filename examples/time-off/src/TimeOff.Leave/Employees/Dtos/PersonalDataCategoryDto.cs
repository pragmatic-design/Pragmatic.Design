namespace TimeOff.Leave.Dtos;

/// <summary>One kind of record held about the employee — their profile, their leave requests — field by field.</summary>
public sealed record PersonalDataCategoryDto(string Category, IReadOnlyList<IReadOnlyDictionary<string, string?>> Records);
