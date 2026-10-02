namespace Casework.Intake.Entities;

/// <summary>
///     An organisation's own letter: where its <c>.pdxdoc</c> is, and when it sent it.
/// </summary>
/// <remarks>
///     <para>
///         <b>A row and not a file name.</b> <c>IFileStorage.SaveAsync</c> chooses the stored name —
///         LocalDisk writes <c>{Guid:N}{ext}</c>, and the URI it returns is provider-defined — so an
///         application that wanted to find a template later by rebuilding its path would be guessing at
///         a shape the contract says is not its to know. What the application keeps is the URI the store
///         gave it, which is exactly what <c>GetAsync</c> takes back.
///     </para>
///     <para>
///         ⚠️ An <c>ITenantEntity</c>, and that is what makes "the tenant's own template" true without a
///         line of code about tenants: the row is in that organisation's database (or behind its tenant
///         column), the query filter is fail-closed, and there is no path by which one organisation's
///         letter can be read while serving another.
///     </para>
///     <para>
///         One row per <see cref="Piece" /> per organisation: the letter and its letterhead are uploaded
///         and replaced separately, because an organisation that only wants its own heading should not
///         have to adopt somebody else's wording to get it.
///     </para>
/// </remarks>
[Entity]
[Audited]
[Auditable]
public partial class LetterTemplate : IEntity, ITenantEntity
{
    public string TenantId { get; set; } = "";

    /// <summary>Which piece of the letter this is.</summary>
    [LogicKey]
    [Required]
    [MaxLength(60)]
    public string Piece { get; private set; } = "";

    /// <summary>Where the store put it. Kept as the store gave it, never rebuilt.</summary>
    [Required]
    [MaxLength(500)]
    public string StoredAt { get; private set; } = "";

    /// <summary>When the organisation last sent this piece in.</summary>
    public DateTimeOffset UploadedOn { get; private set; }

    /// <summary>The letter itself.</summary>
    public const string Letter = "decision-letter";

    /// <summary>The letterhead the letter imports.</summary>
    public const string Header = "header";

    /// <summary>The mail that carries the letter: a <c>.pdxemail</c>.</summary>
    public const string Mail = "decision-mail";

    /// <summary>
    ///     The mail's own letterhead.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Separate from the letter's, because the two markups are different languages: a
    ///     <c>.pdxdoc</c> header is pages and nodes, a <c>.pdxemail</c> one is sections and columns.
    ///     One file for both would be a file that parses in one of the two.
    /// </remarks>
    public const string MailHeader = "mail-header";

    /// <summary>
    ///     A table the organisation keeps beside its letter — its own fees, in a spreadsheet.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not markup: a <c>.csv</c> the organisation exports from wherever it keeps its charges,
    ///         and the letter names it as <c>fees</c>. It is uploaded and replaced on its own, like
    ///         every other piece, because the schedule changes on a different calendar from the wording.
    ///     </para>
    ///     <para>
    ///         ⚠️ This is the piece a data source that takes only a <b>file path</b> cannot serve: what an
    ///         organisation
    ///         uploads has no path — it is in <c>IFileStorage</c>, local disk here and object storage in
    ///         a deployment. Reading it by path would mean going around the abstraction, so the letter
    ///         reads it as a stream from the store (<c>CsvStreamDataSource</c> in
    ///         <c>TheDecisionLetter</c>).
    ///     </para>
    /// </remarks>
    public const string Fees = "fee-table";

    internal static LetterTemplate Uploaded(string piece, string storedAt, DateTimeOffset now)
    {
        var template = Create();
        template.SetPiece(piece);
        template.SetStoredAt(storedAt);
        template.SetUploadedOn(now);

        return template;
    }

    /// <summary>Replaces this piece with a newer file. The old bytes stay where they are.</summary>
    /// <remarks>
    ///     ⚠️ The previous file is <b>not</b> deleted, and that is a decision rather than an omission: a
    ///     letter that was sent to an applicant last week was rendered from it, and an organisation that
    ///     uploads a bad template at 17:00 has something to go back to. Cleaning them up is a retention
    ///     job this example does not have.
    /// </remarks>
    internal void Replace(string storedAt, DateTimeOffset now)
    {
        SetStoredAt(storedAt);
        SetUploadedOn(now);
    }
}
