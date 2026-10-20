using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Fuzz;

/// <summary>
///     The CSV reader on a file a user uploads (#309), through the streaming reader, the materializing one and the
///     model, with the default options, the Italian ones, and limits small enough for a fuzzer's input to cross.
/// </summary>
/// <remarks>
///     The reader's declared outcomes are rows and its own refusal, a <see cref="FormatException" /> whose message
///     starts with <c>CSV</c>. That returns; anything else escapes and is a crash, and so is a disagreement between
///     the three readers on the same bytes (<see cref="FuzzFinding" />).
/// </remarks>
public static class CsvReaderTarget
{
    private static readonly CsvOptions[] Options =
    [
        CsvOptions.Default,
        CsvOptions.Italian,
        CsvOptions.Default with { HasHeaders = false, MaxFieldChars = 16, MaxRows = 8, MaxFieldsPerRow = 8 },
    ];

    public static void Run(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        foreach (var options in Options)
            Read(bytes, options);
    }

    private static void Read(byte[] bytes, CsvOptions options)
    {
        try
        {
            var streamed = CsvReader.ReadRows(new MemoryStream(bytes), options).ToList();
            var (headers, rows) = CsvReader.Read(bytes, options);
            var model = CsvReader.ReadAsModel(bytes, options: options);

            if (streamed.Count != rows.Count + (headers.Length > 0 ? 1 : 0))
                throw new FuzzFinding($"{streamed.Count} rows streamed, {rows.Count} read with {headers.Length} headers.");
            if (model.Sheets.Single().Rows.Count != streamed.Count)
                throw new FuzzFinding($"{streamed.Count} rows streamed, {model.Sheets.Single().Rows.Count} in the model.");
        }
        catch (FormatException e) when (e.Message.StartsWith("CSV ", StringComparison.Ordinal))
        {
        }
    }
}
