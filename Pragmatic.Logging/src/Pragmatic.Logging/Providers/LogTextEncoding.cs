using System.Text;

namespace Pragmatic.Logging.Providers;

/// <summary>The encoding every provider that writes text writes it in.</summary>
/// <remarks>
///     UTF-8 without a byte order mark. <c>Encoding.UTF8</c> carries the BOM as its preamble, so a
///     <see cref="StreamWriter" /> built with it puts EF BB BF in front of the first record of every new
///     file or stream. A JSON-lines reader fails on that record, and so does any log shipper that parses
///     line by line. Nothing that reads log output expects one, so there is no option to bring it back.
/// </remarks>
internal static class LogTextEncoding
{
    /// <summary>UTF-8, with no byte order mark.</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
