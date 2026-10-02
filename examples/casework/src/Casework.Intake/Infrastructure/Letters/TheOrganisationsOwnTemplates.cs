using Casework.Intake.Letters;
using Pragmatic.Documents.Markup;
using Pragmatic.Persistence.Repository;
using Pragmatic.Storage;

namespace Casework.Intake.Infrastructure.Letters;

/// <summary>
///     The pieces an organisation uploaded — asked before the application's own, piece by piece.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The row is read through the ordinary tenant-filtered repository and the URI comes off it —
///         nothing composes a path. Which is what makes "this organisation's template" a property of the
///         query filter rather than of a string somebody built.
///     </para>
///     <para>
///         A row pointing at bytes that are gone answers nothing, so the application's piece is used:
///         the letter still has to go out, and <see cref="Supplied" /> reports that it was not theirs.
///     </para>
/// </remarks>
internal sealed class TheOrganisationsOwnTemplates(
    IReadRepository<LetterTemplate> templates,
    IFileStorage files) : IPdxTemplateSource
{
    private readonly HashSet<string> _supplied = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this organisation's own markup answered for <paramref name="name" />.</summary>
    public bool Supplied(string name) => _supplied.Contains(name);

    public async ValueTask<string?> FindAsync(string name, CancellationToken ct = default)
    {
        var piece = Path.GetFileNameWithoutExtension(name);
        var row = await templates
            .FirstOrDefaultAsync(LetterTemplateSpecifications.ThePiece(piece), ct)
            .ConfigureAwait(false);

        if (row is null)
            return null;

        var stored = await files
            .GetAsync(new Uri(row.StoredAt, UriKind.RelativeOrAbsolute), ct)
            .ConfigureAwait(false);

        if (stored is null)
            return null;

        await using (stored.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stored);
            var markup = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            _supplied.Add(name);

            return markup;
        }
    }

    public override string ToString() => "the organisation's uploaded templates";
}
