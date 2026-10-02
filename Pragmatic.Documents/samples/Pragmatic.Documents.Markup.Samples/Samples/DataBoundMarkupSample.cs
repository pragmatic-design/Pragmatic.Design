using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Markup.Samples.Samples;

/// <summary>
///     <c>templates/invoice.pdxdoc</c> against its data, in two languages: a partial (the letterhead),
///     <c>t:</c> translations with parameters, the date and currency pipes, a table over a collection, an
///     aggregate, and an <c>if</c>.
/// </summary>
/// <remarks>
///     The language is an argument of every call, because it is the reader's: the same invoice goes to an
///     Italian customer and to an English one, and nothing ambient decides which.
/// </remarks>
public static class DataBoundMarkupSample
{
    public static async Task Run(IServiceProvider services, string outputDir)
    {
        Console.WriteLine("--- Data-bound template (templates/invoice.pdxdoc, two languages) ---");

        await using var scope = services.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        foreach (var culture in new[] { "it-IT", "en-US" })
        {
            var document = await templates.DocumentAsync("invoice.pdxdoc", culture, Data());
            var bytes = PdfRenderer.Render(document.Model);

            var path = Path.Combine(outputDir, $"invoice.{culture}.pdf");
            await File.WriteAllBytesAsync(path, bytes);
            Console.WriteLine($"  invoice.{culture}.pdf    {bytes.Length} bytes, title \"{document.Model.Title}\", {document.Warnings.Count} warning(s)");
        }

        Console.WriteLine();
    }

    private static TemplateDataContext Data() => new TemplateDataContext()
        .AddSource("company", new Dictionary<string, object?>
        {
            ["name"] = "PRAGMATIC S.R.L.",
            ["address"] = "Via Roma 42, 20121 Milano",
        })
        .AddSource("customer", new Dictionary<string, object?>
        {
            ["name"] = "Mario Rossi",
            ["vatId"] = "IT98765432101",
        })
        .AddSource("invoice", new Dictionary<string, object?>
        {
            ["number"] = "2026-042",
            ["date"] = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.FromHours(2)),
            ["vat"] = 1320m,
            ["total"] = 7320m,
            ["note"] = "",
            ["items"] = new List<Dictionary<string, object?>>
            {
                new() { ["description"] = "Pragmatic Enterprise license", ["quantity"] = 1, ["price"] = 4000m, ["amount"] = 4000m },
                new() { ["description"] = "Premium support (annual)",      ["quantity"] = 1, ["price"] = 1200m, ["amount"] = 1200m },
                new() { ["description"] = "Team training (2 days)",        ["quantity"] = 1, ["price"] =  800m, ["amount"] =  800m },
            },
        });
}
