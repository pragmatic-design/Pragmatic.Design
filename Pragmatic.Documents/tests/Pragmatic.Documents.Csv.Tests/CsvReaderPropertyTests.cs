using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv.Tests;

/// <summary>
///     The CSV reader parses files an application's users upload (#158, #309). For any input it has two outcomes and
///     no others: rows, or its own refusal, a <see cref="FormatException" /> whose message starts with <c>CSV</c>, for
///     a quoted field left open at the end of the input, a field over <see cref="CsvOptions.MaxFieldChars" /> or rows
///     over <see cref="CsvOptions.MaxRows" />. Bytes that are not UTF-8 are read as U+FFFD, not refused.
/// </summary>
public class CsvReaderPropertyTests
{
    // What a field can hold that the format has to deal with: both delimiters, quotes, both line endings and their
    // halves, whitespace a parser might trim, and characters outside ASCII and outside the BMP.
    private static readonly string[] Pieces = ["a", "b", ",", ";", "\"", "\r", "\n", "\r\n", " ", "\t", "=", "é", "😀"];

    private static readonly Gen<string> Fields = Gen.Elements(Pieces).ListOf().Select(pieces => string.Concat(pieces));

    private static readonly Gen<List<string[]>> Rows =
        Fields.NonEmptyListOf().Select(fields => fields.ToArray()).ListOf().Select(rows => rows.ToList());

    private static readonly Gen<CsvOptions> Options =
        from delimiter in Gen.Elements(',', ';', '\t')
        from maxFieldChars in Gen.Frequency((1, Gen.Constant(CsvOptions.Default.MaxFieldChars)), (2, Gen.Choose(0, 12)))
        from maxRows in Gen.Frequency((1, Gen.Constant(CsvOptions.Default.MaxRows)), (2, Gen.Choose(0, 6)))
        select new CsvOptions
        {
            Delimiter = delimiter, HasHeaders = false, FormulaProtection = false, MaxFieldChars = maxFieldChars, MaxRows = maxRows,
        };

    [Fact]
    public void Any_bytes_read_to_a_declared_outcome()
        => Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), bytes => IsDeclared(bytes, CsvOptions.Default) && IsDeclared(bytes, CsvOptions.Italian))
            .QuickCheckThrowOnFailure();

    [Fact]
    public void Any_text_of_the_format_s_own_characters_reads_to_a_declared_outcome()
        => Prop.ForAll(Gen.Elements(Pieces).ListOf().Select(pieces => Encoding.UTF8.GetBytes(string.Concat(pieces))).ToArbitrary(),
                bytes => IsDeclared(bytes, CsvOptions.Default) && IsDeclared(bytes, CsvOptions.Italian))
            .Check(Config.QuickThrowOnFailure.WithMaxTest(500));

    /// <summary>
    ///     What the writer writes reads back as written, or is refused when, and only when, it is over a limit.
    /// </summary>
    [Fact]
    public void Written_rows_read_back_or_are_refused_for_exactly_the_limit_they_cross()
        => Prop.ForAll(Rows.ToArbitrary(), Options.ToArbitrary(), (rows, options) =>
        {
            var csv = CsvWriter.WriteToArray([], [.. rows.Select(row => (IReadOnlyList<string?>)row)], options);
            var overALimit = rows.Count > options.MaxRows || rows.Any(row => row.Any(field => field.Length > options.MaxFieldChars));

            try
            {
                var read = CsvReader.ReadRows(new MemoryStream(csv), options).ToList();
                return !overALimit && read.Count == rows.Count && read.Zip(rows).All(pair => pair.First.SequenceEqual(pair.Second));
            }
            catch (FormatException e) when (e.Message.StartsWith("CSV ", StringComparison.Ordinal))
            {
                return overALimit;
            }
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(500));

    /// <summary>A quoted field the input never closes is refused, wherever it starts and whatever precedes it.</summary>
    [Fact]
    public void A_quoted_field_left_open_is_refused()
    {
        var unquoted = Gen.Elements(Pieces.Where(piece => piece != "\"").ToArray()).ListOf().Select(pieces => string.Concat(pieces));

        Prop.ForAll(Rows.ToArbitrary(), unquoted.ToArbitrary(), (rows, tail) =>
        {
            var options = CsvOptions.Default with { HasHeaders = false, FormulaProtection = false };
            var written = CsvWriter.WriteToArray([], [.. rows.Select(row => (IReadOnlyList<string?>)row)], options);
            byte[] csv = [.. written, .. Encoding.UTF8.GetBytes("\"" + tail)];

            try
            {
                CsvReader.ReadRows(new MemoryStream(csv), options).ToList();
                return false;
            }
            catch (FormatException e)
            {
                return e.Message.Contains("unclosed quoted field", StringComparison.Ordinal);
            }
        }).QuickCheckThrowOnFailure();
    }

    // Rows, the same rows from the materializing reader, and a model of them; or the reader's refusal.
    private static bool IsDeclared(byte[] bytes, CsvOptions options)
    {
        try
        {
            var streamed = CsvReader.ReadRows(new MemoryStream(bytes), options).ToList();
            var (headers, rows) = CsvReader.Read(bytes, options);
            var model = CsvReader.ReadAsModel(bytes, options: options);

            return streamed.Count == rows.Count + (headers.Length > 0 ? 1 : 0)
                   && model.Sheets.Single().Rows.Count == streamed.Count;
        }
        catch (FormatException e)
        {
            return e.Message.StartsWith("CSV ", StringComparison.Ordinal);
        }
    }
}
