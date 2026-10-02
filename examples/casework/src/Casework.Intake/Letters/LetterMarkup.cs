using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Markup;

namespace Casework.Intake.Letters;

/// <summary>
///     What the application knows about letter markup: where an organisation's pieces are kept, and
///     whether a file it sent in can be read at all.
/// </summary>
internal static class LetterMarkup
{
    /// <summary>
    ///     The container an organisation's own pieces are stored in.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The tenant is in the path <b>and</b> in the row: the row is what the application reads (it
    ///     is an <c>ITenantEntity</c>, so the filter answers "whose"), and the path is what an operator
    ///     sees on disk. Neither is load-bearing on its own — a container alone would be a convention a
    ///     bug could walk out of, which is why nothing here ever composes a path to <em>read</em> by.
    /// </remarks>
    public static string ContainerFor(string tenantId) => $"letters/{tenantId}";

    /// <summary>
    ///     The extension a piece is stored under — which is also what tells an operator, looking at the
    ///     container, which of the two markups a file is.
    /// </summary>
    public static string ExtensionOf(string piece) => piece switch
    {
        LetterTemplate.Mail or LetterTemplate.MailHeader => ".pdxemail",
        LetterTemplate.Fees => ".csv",
        _ => ".pdxdoc",
    };

    /// <summary>
    ///     Whether this markup parses, by the rules of the piece it is. The failure is the parser's own
    ///     message, not a paraphrase.
    /// </summary>
    /// <remarks>
    ///     It parses and throws the result away: what is wanted here is the <b>yes or no</b>, before a
    ///     row says an organisation has a letter. Parsing again at render time costs a millisecond and
    ///     keeps this from being a cache that can go stale against the file it describes.
    /// </remarks>
    public static bool Parses(string piece, string markup, out string whyNot)
    {
        try
        {
            // ⚠️ The fee table is not markup, so there is no parser to run and "does it parse" is the
            // wrong question. What would ruin a letter is an empty file or one with a header and no
            // rows — the letter then quotes a table with nothing in it, which looks like a rendering
            // fault and is an upload fault. So the check is that it has a header and at least one row,
            // asked here rather than at 17:00 on the day a decision goes out.
            if (piece == LetterTemplate.Fees)
            {
                using var csv = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(markup));
                var table = CsvReader.ReadAsModel(csv, LetterTemplate.Fees);

                if (table.Sheets.Count == 0 || table.Sheets[0].Rows.Count < 2)
                {
                    whyNot = "A fee table needs a header row and at least one fee.";

                    return false;
                }

                whyNot = "";

                return true;
            }

            // ⚠️ Which parser depends on the piece: a letter is a document, a mail is a mail, and the
            // two markups are different languages with the same look. Parsing a .pdxemail with the
            // document parser fails on the root element, which is the failure this method exists to
            // report — but reporting "root element must be <document>" to somebody who uploaded a
            // perfectly good mail would be a message that sends them the wrong way.
            if (piece is LetterTemplate.Mail or LetterTemplate.MailHeader)
                PdxEmailParser.Parse(markup);
            else
                PdxDocParser.Parse(markup);

            whyNot = "";

            return true;
        }
        catch (Exception failed) when (failed is MarkupParseException or System.Xml.XmlException)
        {
            whyNot = failed.Message;

            return false;
        }
    }
}
