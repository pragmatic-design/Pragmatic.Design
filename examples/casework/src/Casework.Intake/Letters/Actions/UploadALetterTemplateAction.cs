using Microsoft.AspNetCore.Http;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Storage;
using Pragmatic.Validation.Types;

namespace Casework.Intake.Letters.Actions;

/// <summary>
///     An organisation sends in its own letter — or just its letterhead.
/// </summary>
/// <remarks>
///     <para>
///         <b>This is what "without a deployment" means.</b> The wording, the order of the paragraphs and
///         the letterhead are in a file the organisation uploads; nothing here knows what the letter
///         says.
///     </para>
///     <para>
///         ⚠️ The markup is <b>parsed here</b>, before the row is written, and a template that does not
///         parse is refused. The alternative is a file that is accepted at 17:00 and discovered at the
///         moment a decision letter has to go out — parsing costs a millisecond and moves the failure to
///         the person who can fix it.
///     </para>
///     <para>
///         ⚠️ What is <b>not</b> checked here is whether the expressions name anything real: a template
///         that asks for <c>{{ case.reference }}</c> parses perfectly and renders a hole. That is not a
///         parse error and cannot be one — the data is not here — so it is caught where the data is, by
///         reading the resolver's warnings (<c>TheDecisionLetter</c>).
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(IntakePermissions.LetterTemplate.Create)]
[Endpoint(HttpVerb.Post, "api/letter-templates")]
public partial class UploadALetterTemplateAction : DomainAction<LetterTemplateDto, IError>
{
    private IFileStorage _files = null!;
    private IRepository<LetterTemplate> _templates = null!;
    private ITenantContext _organisation = null!;

    /// <summary>
    ///     Which piece is being replaced: <c>decision-letter</c>, <c>header</c>, <c>decision-mail</c>,
    ///     <c>mail-header</c> or <c>fee-table</c>.
    /// </summary>
    // ⚠️ [FromForm] is redundant here — a multipart request has no JSON body, so an
    // unmarked property is bound from the form too — and kept because it is explicit: a reader sees
    // where the value comes from without knowing the rule. Measured: the host compiles without it.
    [FromForm]
    [Required]
    [MaxLength(60)]
    public required string Piece { get; init; }

    /// <summary>The <c>.pdxdoc</c> itself.</summary>
    [FromForm]
    [MaxFileSize(1024 * 1024)]
    // text/csv is here for the fee table, which is the one piece that is not markup.
    [AllowedContentTypes(
        "application/xml", "text/xml", "text/plain", "text/csv", "application/octet-stream")]
    public required IFormFile File { get; init; }

    /// <summary>When it arrived: the application's clock.</summary>
    [FromClock]
    public DateTimeOffset UploadedOn { get; private set; }

    public override async Task<Result<LetterTemplateDto, IError>> Execute(CancellationToken ct = default)
    {
        if (Piece is not (LetterTemplate.Letter or LetterTemplate.Header
            or LetterTemplate.Mail or LetterTemplate.MailHeader or LetterTemplate.Fees))
            return Result<LetterTemplateDto, IError>.Failure(
                ValidationError.For(nameof(Piece), "error.letter.piece.unknown"));

        using var buffered = new MemoryStream();
        var upload = File.OpenReadStream();
        await using (upload.ConfigureAwait(false))
            await upload.CopyToAsync(buffered, ct).ConfigureAwait(false);

        var markup = System.Text.Encoding.UTF8.GetString(buffered.ToArray());
        if (!LetterMarkup.Parses(Piece, markup, out var whyNot))
            return Result<LetterTemplateDto, IError>.Failure(
                ValidationError.For(nameof(File), "error.letter.markup.unreadable", ("reason", whyNot)));

        buffered.Position = 0;
        var stored = await _files
            .SaveAsync(
                buffered, $"{Piece}{LetterMarkup.ExtensionOf(Piece)}",
                LetterMarkup.ContainerFor(_organisation.TenantId ?? ""), ct)
            .ConfigureAwait(false);

        // One row per piece per organisation: the second upload replaces the first rather than adding
        // a second letter nobody chose between.
        var existing = await _templates
            .FirstOrDefaultAsync(LetterTemplateSpecifications.ThePiece(Piece), ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Replace(stored.ToString(), UploadedOn);

            return LetterTemplateDto.FromEntity(existing);
        }

        var template = LetterTemplate.Uploaded(Piece, stored.ToString(), UploadedOn);
        _templates.Add(template);

        return LetterTemplateDto.FromEntity(template);
    }
}
