namespace Casework.Intake.Dtos;

/// <summary>What an organisation gets back after sending in a piece of its letter.</summary>
/// <remarks>
///     ⚠️ No <c>StoredAt</c>: where the bytes are is the application's business, and an organisation
///     that received the URI would start fetching it directly — past the tenant filter that makes the
///     template theirs.
/// </remarks>
[MapFrom<LetterTemplate>]
public sealed partial class LetterTemplateDto
{
    public Guid Id { get; init; }

    /// <summary>Which piece was replaced: the letter, or the letterhead.</summary>
    public string Piece { get; init; } = "";

    public DateTimeOffset UploadedOn { get; init; }
}
