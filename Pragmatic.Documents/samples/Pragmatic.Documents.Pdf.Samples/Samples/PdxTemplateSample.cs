using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
///     The recommended way to a PDF: a <c>.pdxdoc</c> template (<c>templates/receipt.pdxdoc</c>, embedded
///     in this assembly) resolved by <see cref="IPdxTemplates" /> in the reader's language, then rendered.
///     The other samples build the model in code — the way for what the markup cannot say.
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
        Console.WriteLine("--- PDX template (templates/receipt.pdxdoc) -> PDF ---");

        var translations = new InMemoryLocalizationProvider().AddStrings("it", new Dictionary<string, string>
        {
            ["receipt.title"] = "Ricevuta {number}",
            ["receipt.issued"] = "Emessa il {date}",
            ["receipt.item"] = "Articolo",
            ["receipt.amount"] = "Importo",
            ["receipt.total"] = "Totale",
        });

        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizer>(new StringLocalizer(translations, new I18NOptions()));
        services.AddPdxTemplates(t => t.FromAssemblyOf<PdxTemplateSampleMarker>());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        var data = new TemplateDataContext()
            .AddSource("shop", new Dictionary<string, object?> { ["name"] = "Bottega Pragmatica" })
            .AddSource("receipt", new Dictionary<string, object?>
            {
                ["number"] = "R-0917",
                ["issuedOn"] = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.FromHours(2)),
                ["lines"] = new List<Dictionary<string, object?>>
                {
                    new() { ["name"] = "Espresso",  ["amount"] = 1.20m },
                    new() { ["name"] = "Cornetto",  ["amount"] = 1.50m },
                    new() { ["name"] = "Spremuta",  ["amount"] = 3.00m },
                },
                ["payments"] = new List<Dictionary<string, object?>>
                {
                    new() { ["method"] = "Contanti", ["amount"] = 5.70m },
                },
            });

        var document = await templates.DocumentAsync("receipt.pdxdoc", "it-IT", data);
        var bytes = PdfRenderer.Render(document.Model);

        var path = Path.Combine(outputDir, "receipt-from-template.pdf");
        await File.WriteAllBytesAsync(path, bytes);
        Console.WriteLine($"  receipt-from-template.pdf  {bytes.Length} bytes, title \"{document.Model.Title}\", {document.Warnings.Count} warning(s)");
        Console.WriteLine();
    }
}
