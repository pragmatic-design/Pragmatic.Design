using PdfSharp.Fonts;

namespace Pragmatic.Documents.Benchmarks.Competitors;

/// <summary>
///     Gives PDFsharp one sans-serif face from the machine's own fonts, as Pragmatic's renderer takes its fonts
///     from the system: Arial on Windows, DejaVu Sans on Linux (the package Pragmatic's renderer needs there).
/// </summary>
/// <remarks>
///     PDFsharp's core build finds no font by itself outside Windows, and a resolver is the documented way to
///     give it one. Every family resolves to the same face, so both libraries set the table in one font.
/// </remarks>
public sealed class SystemFontResolver : IFontResolver
{
    private const string Regular = "regular";
    private const string Bold = "bold";

    private static readonly (string Regular, string Bold)[] Candidates =
    [
        (@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf"),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
    ];

    private readonly byte[] _regular;
    private readonly byte[] _bold;

    public SystemFontResolver()
    {
        foreach (var (regular, bold) in Candidates)
        {
            if (File.Exists(regular) && File.Exists(bold))
            {
                _regular = File.ReadAllBytes(regular);
                _bold = File.ReadAllBytes(bold);

                return;
            }
        }

        throw new InvalidOperationException(
            "No font for PDFsharp: neither Arial (Windows) nor DejaVu Sans (Linux, fonts-dejavu-core) was found.");
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        => new(bold ? Bold : Regular);

    public byte[] GetFont(string faceName) => faceName == Bold ? _bold : _regular;
}
