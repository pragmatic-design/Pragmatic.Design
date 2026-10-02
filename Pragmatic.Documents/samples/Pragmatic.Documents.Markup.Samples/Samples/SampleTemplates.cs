using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Markup.Samples.Samples;

/// <summary>
///     The wiring an application does once: the templates embedded in this assembly, and the translations
///     their <c>t:</c> keys read.
/// </summary>
/// <remarks>
///     In an application built on Pragmatic.Composition none of this is written by hand: a module declares
///     <c>[assembly: PdxTemplates&lt;TModule&gt;]</c> and the generated host calls <c>AddPdxTemplates</c>, and
///     the translations come from the module's JSON files. A console sample has no host, so it says both
///     here.
/// </remarks>
public static class SampleTemplates
{
    public static ServiceProvider Build()
    {
        var translations = new InMemoryLocalizationProvider()
            .AddStrings("en", new Dictionary<string, string>
            {
                ["invoice.title"] = "Invoice {number}",
                ["invoice.issued"] = "Issued on {date}",
                ["invoice.customer"] = "Customer: {name} — VAT {vat}",
                ["invoice.description"] = "Description",
                ["invoice.quantity"] = "Qty",
                ["invoice.price"] = "Price",
                ["invoice.amount"] = "Amount",
                ["invoice.subtotal"] = "Subtotal",
                ["invoice.vat"] = "VAT 22%",
                ["invoice.total"] = "TOTAL",
            })
            .AddStrings("it", new Dictionary<string, string>
            {
                ["invoice.title"] = "Fattura {number}",
                ["invoice.issued"] = "Emessa il {date}",
                ["invoice.customer"] = "Cliente: {name} — P. IVA {vat}",
                ["invoice.description"] = "Descrizione",
                ["invoice.quantity"] = "Qtà",
                ["invoice.price"] = "Prezzo",
                ["invoice.amount"] = "Importo",
                ["invoice.subtotal"] = "Imponibile",
                ["invoice.vat"] = "IVA 22%",
                ["invoice.total"] = "TOTALE",
            });

        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizer>(new StringLocalizer(translations, new I18NOptions()));
        services.AddPdxTemplates(t => t.FromAssemblyOf<SampleTemplatesMarker>());
        return services.BuildServiceProvider();
    }
}
