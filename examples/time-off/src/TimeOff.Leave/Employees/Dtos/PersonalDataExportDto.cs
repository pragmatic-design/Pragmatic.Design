using System.Globalization;

namespace TimeOff.Leave.Dtos;

/// <summary>
///     Everything the application holds about the employee who asked: each kind of record, and what they
///     agreed to.
/// </summary>
/// <remarks>
///     The values as text, whatever their type: an export is read by a person or carried to another
///     system, and neither is served by a type the reader has to know.
/// </remarks>
public sealed record PersonalDataExportDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PersonalDataCategoryDto> Categories,
    IReadOnlyList<ConsentDto> Consents)
{
    internal static PersonalDataExportDto From(SubjectDataExport export) => new(
        export.GeneratedAt,
        [
            .. export.Categories
                .OrderBy(category => category.Key, StringComparer.Ordinal)
                .Select(category => new PersonalDataCategoryDto(category.Key, [.. category.Value.Select(AsText)]))
        ],
        [.. export.Consents.Select(ConsentDto.Selector)]);

    private static IReadOnlyDictionary<string, string?> AsText(IReadOnlyDictionary<string, object?> record) =>
        record.ToDictionary(field => field.Key, field => Convert.ToString(field.Value, CultureInfo.InvariantCulture));
}
