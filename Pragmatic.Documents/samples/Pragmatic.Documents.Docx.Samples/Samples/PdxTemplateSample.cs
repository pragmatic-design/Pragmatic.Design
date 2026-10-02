using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Docx.Samples.Samples;

/// <summary>
///     The recommended way to a DOCX: a <c>.pdxdoc</c> template (<c>templates/letter.pdxdoc</c>, embedded in
///     this assembly) resolved by <see cref="IPdxTemplates" /> in the recipient's language, then rendered.
///     The other samples build the model in code — the way for what the markup cannot say (a table of
///     contents, footnotes, fields, bookmarks).
/// </summary>
/// <remarks>
///     In an application the wiring below is generated: a module declares
///     <c>[assembly: PdxTemplates&lt;TModule&gt;]</c>, the host calls <c>AddPdxTemplates</c>, and the
///     translations come from the module's JSON files.
/// </remarks>
public static class PdxTemplateSample
{
    public static async Task Run(string outputDir)
    {
        Console.WriteLine("--- PDX template (templates/letter.pdxdoc) -> DOCX ---");

        var translations = new InMemoryLocalizationProvider().AddStrings("it", new Dictionary<string, string>
        {
            ["letter.subject"] = "Rinnovo del contratto {number}",
            ["letter.greeting"] = "Gentile {name},",
            ["letter.body"] = "il contratto si rinnova dal {start} con un canone annuo di {fee}, che comprende:",
            ["letter.discount"] = "Per la fedeltà di questi anni applichiamo uno sconto del {discount}.",
            ["letter.closing"] = "Cordiali saluti,",
        });

        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizer>(new StringLocalizer(translations, new I18NOptions()));
        services.AddPdxTemplates(t => t.FromAssemblyOf<PdxTemplateSampleMarker>());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        var data = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?>
            {
                ["name"] = "PRAGMATIC S.R.L.",
                ["city"] = "Milano",
                ["signatory"] = "Giulia Neri, Customer Success",
            })
            .AddSource("recipient", new Dictionary<string, object?>
            {
                ["name"] = "Mario Rossi",
                ["address"] = "Corso Italia 7, 10121 Torino",
            })
            .AddSource("letter", new Dictionary<string, object?>
            {
                ["date"] = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.FromHours(2)),
            })
            .AddSource("contract", new Dictionary<string, object?>
            {
                ["number"] = "C-2024-118",
                ["start"] = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(1)),
                ["fee"] = 4800m,
                ["discount"] = 0.1m,
                ["services"] = new List<string> { "Licenza Enterprise", "Supporto prioritario", "Due giornate di formazione" },
            });

        var document = await templates.DocumentAsync("letter.pdxdoc", "it-IT", data);
        var bytes = DocxRenderer.Render(document.Model);

        var path = Path.Combine(outputDir, "letter-from-template.docx");
        await File.WriteAllBytesAsync(path, bytes);
        Console.WriteLine($"  letter-from-template.docx  {bytes.Length} bytes, title \"{document.Model.Title}\", {document.Warnings.Count} warning(s)");
        Console.WriteLine();
    }
}
