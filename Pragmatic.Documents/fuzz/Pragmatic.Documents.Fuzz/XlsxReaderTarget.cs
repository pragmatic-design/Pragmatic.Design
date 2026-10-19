using System.Xml;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Fuzz;

/// <summary>
///     The XLSX reader on a file a user uploads (#308).
/// </summary>
/// <remarks>
///     The reader's declared outcomes are a model, an <see cref="InvalidDataException" /> for an archive that is not a
///     ZIP or is damaged, an <see cref="XmlException" /> for a part that is not well-formed XML, and its own refusal, an
///     <see cref="InvalidOperationException" /> whose message starts with <c>XLSX</c>. Those return; anything else
///     escapes and is a crash, an <see cref="InvalidOperationException" /> the reader did not write included.
/// </remarks>
public static class XlsxReaderTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        try
        {
            XlsxReader.Read(data.ToArray());
        }
        catch (InvalidDataException)
        {
        }
        catch (XmlException)
        {
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("XLSX ", StringComparison.Ordinal))
        {
        }
    }
}
