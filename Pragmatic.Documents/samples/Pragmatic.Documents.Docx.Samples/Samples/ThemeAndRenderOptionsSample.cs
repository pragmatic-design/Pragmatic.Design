using Pragmatic.Documents.Docx;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Samples.Samples;

/// <summary>
///     Customising DOCX output with <see cref="DocxTheme"/> and
///     <see cref="DocxRenderOptions"/>. The theme controls fonts, colours and
///     spacing (feeds styles.xml / theme1.xml); the options control field-update
///     behaviour and the deterministic timestamp used for DATE/TIME fields.
///     Shows both a built-in preset (DocxTheme.Formal) and a hand-rolled theme.
/// </summary>
public static class ThemeAndRenderOptionsSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Custom DocxTheme + DocxRenderOptions ---");

        var model = new DocumentBuilder()
            .Title("Branded Report")
            .Page(p => p
                .Heading("Branded Report", level: 1)
                .Text("This document uses a custom theme (fonts + colours + spacing) and render options.")
                .Heading("Section", level: 2)
                .Text("Heading colours, body font and line spacing all come from the DocxTheme."))
            .Build();


        // 1) Built-in preset theme.
        var formalBytes = DocxRenderer.Render(model, options: new DocxRenderOptions { Theme = DocxTheme.Formal });
        var formalPath = Path.Combine(outputDir, "themed-formal.docx");
        File.WriteAllBytes(formalPath, formalBytes);
        Console.WriteLine($"  themed-formal.docx     {formalBytes.Length} bytes (DocxTheme.Formal preset)");

        // 2) Hand-rolled theme + deterministic render timestamp.
        var customTheme = new DocxTheme
        {
            BodyFont = "Georgia",
            HeadingFont = "Arial Black",
            PrimaryColor = "8E44AD",        // H1-H2 colour (hex without #)
            SecondaryColor = "5B2C6F",      // H3+ colour
            AccentColor = "8E44AD",
            BodyFontSize = 24,              // half-points -> 12pt
            DefaultLineSpacing = 276,       // 1.15x
        };

        var options = new DocxRenderOptions
        {
            Theme = customTheme,
            UpdateFieldsOnOpen = true,
            RenderTimestamp = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
        };

        var customBytes = DocxRenderer.Render(model, options: options);
        var customPath = Path.Combine(outputDir, "themed-custom.docx");
        File.WriteAllBytes(customPath, customBytes);
        Console.WriteLine($"  themed-custom.docx     {customBytes.Length} bytes (body={customTheme.BodyFont}, heading={customTheme.HeadingFont}, H1 colour=#{customTheme.PrimaryColor})");
        Console.WriteLine();
    }
}
