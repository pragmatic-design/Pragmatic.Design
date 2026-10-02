using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Email.Samples.Samples;

/// <summary>
///     The recommended way to a mail: <c>templates/invoice-paid.pdxemail</c>, embedded in this assembly, found
///     by name through <see cref="IPdxTemplates" /> and resolved in the recipient's language — subject,
///     preheader, HTML and plain text from one file, the header an imported partial.
/// </summary>
/// <remarks>
///     In an application the wiring below is generated: a module declares
///     <c>[assembly: PdxTemplates&lt;TModule&gt;]</c>, the host calls <c>AddPdxTemplates</c>, and the
///     translations come from the module's JSON files. The other samples build the model in code.
/// </remarks>
public static class MarkupEmailSample
{
    public static async Task Run(string outputDir)
    {
        Console.WriteLine("--- PDX mail template (templates/invoice-paid.pdxemail, two languages) ---");

        var translations = new InMemoryLocalizationProvider()
            .AddStrings("en", new Dictionary<string, string>
            {
                ["paid.subject"] = "Invoice {number} is paid",
                ["paid.preheader"] = "Received on {date}",
                ["paid.thanks"] = "Thank you, {name}",
                ["paid.received"] = "Your payment of {amount} was received on {date}.",
                ["paid.download"] = "Download the invoice",
            })
            .AddStrings("it", new Dictionary<string, string>
            {
                ["paid.subject"] = "La fattura {number} è pagata",
                ["paid.preheader"] = "Ricevuto il {date}",
                ["paid.thanks"] = "Grazie, {name}",
                ["paid.received"] = "Il pagamento di {amount} è stato ricevuto il {date}.",
                ["paid.download"] = "Scarica la fattura",
            });

        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizer>(new StringLocalizer(translations, new I18NOptions()));
        services.AddPdxTemplates(t => t.FromAssemblyOf<MarkupEmailSampleMarker>());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        foreach (var culture in new[] { "it-IT", "en-US" })
        {
            var mail = await templates.EmailAsync("invoice-paid.pdxemail", culture, Data());

            var html = Path.Combine(outputDir, $"invoice-paid.{culture}.html");
            await File.WriteAllTextAsync(html, mail.Html);
            await File.WriteAllTextAsync(Path.ChangeExtension(html, ".txt"), mail.Text);
            Console.WriteLine($"  [{culture}] subject \"{mail.Subject}\" — {mail.Html.Length} chars HTML, {mail.Text.Length} chars text, {mail.Warnings.Count} warning(s)");
        }

        Console.WriteLine();
    }

    private static TemplateDataContext Data() => new TemplateDataContext()
        .AddSource("hotel", new Dictionary<string, object?>
        {
            ["name"] = "Hotel Milano",
            ["address"] = "Via Roma 42, 20121 Milano",
        })
        .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Alice Bianchi" })
        .AddSource("invoice", new Dictionary<string, object?>
        {
            ["number"] = "INV-9001",
            ["amount"] = 731.50m,
            ["paidAt"] = new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.FromHours(2)),
            ["note"] = "",
        });
}
