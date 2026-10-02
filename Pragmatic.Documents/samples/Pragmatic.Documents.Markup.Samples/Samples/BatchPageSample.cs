using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Markup.Samples.Samples;

/// <summary>
///     <c>templates/payslips.pdxdoc</c>: one &lt;page&gt; repeated once per item of
///     <c>page-data-source</c>. One PDF, one page per employee — payslips, certificates, badges, labels.
/// </summary>
public static class BatchPageSample
{
    public static async Task Run(IServiceProvider services, string outputDir)
    {
        Console.WriteLine("--- Batch pages (templates/payslips.pdxdoc, one page per employee) ---");

        await using var scope = services.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        var payDate = new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.FromHours(2));
        var data = new TemplateDataContext()
            .AddSource("employees", new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["id"] = "E-001", ["name"] = "Alice Bianchi",  ["department"] = "Engineering",
                    ["gross"] = 4500m, ["deductions"] = 1350m, ["net"] = 3150m, ["paidOn"] = payDate,
                },
                new()
                {
                    ["id"] = "E-002", ["name"] = "Bruno Costa",    ["department"] = "Sales",
                    ["gross"] = 3800m, ["deductions"] = 1140m, ["net"] = 2660m, ["paidOn"] = payDate,
                },
                new()
                {
                    ["id"] = "E-003", ["name"] = "Carla De Luca",  ["department"] = "Support",
                    ["gross"] = 3200m, ["deductions"] =  960m, ["net"] = 2240m, ["paidOn"] = payDate,
                },
                new()
                {
                    ["id"] = "E-004", ["name"] = "Davide Esposito", ["department"] = "Engineering",
                    ["gross"] = 4800m, ["deductions"] = 1440m, ["net"] = 3360m, ["paidOn"] = payDate,
                },
            });

        var document = await templates.DocumentAsync("payslips.pdxdoc", "it-IT", data);
        var bytes = PdfRenderer.Render(document.Model);

        var path = Path.Combine(outputDir, "payslips.pdf");
        await File.WriteAllBytesAsync(path, bytes);
        Console.WriteLine($"  payslips.pdf           {bytes.Length} bytes ({document.Model.Pages.Count} pages, one per employee)");
        Console.WriteLine();
    }
}
