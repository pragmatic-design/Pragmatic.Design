namespace Casework.Intake.Dtos;

/// <summary>
///     A document of a case, as the operator who attached it sees it.
/// </summary>
/// <remarks>
///     The storage key is <b>not</b> on it, and that is the point of having a DTO here at all: where the
///     bytes live is the application's business, and a key on the wire is an invitation to compose
///     another one. What a client needs is the id to ask for it by and the hash to check it against.
/// </remarks>
[MapFrom<CaseDocument>]
[GenerateProjection]
public partial class CaseDocumentDto
{
    public Guid Id { get; init; }

    public string FileName { get; init; } = "";

    public string ContentType { get; init; } = "";

    public long Length { get; init; }

    public string Sha256 { get; init; } = "";

    public DateTimeOffset UploadedOn { get; init; }
}
