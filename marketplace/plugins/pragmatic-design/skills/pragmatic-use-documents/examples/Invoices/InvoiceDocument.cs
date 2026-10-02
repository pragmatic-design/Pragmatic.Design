using Invoicing.Registry.Dtos;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Ensure;

namespace Invoicing.Billing.Invoices;

/// <summary>
///     The document an invoice is: <c>templates/invoice.pdxdoc</c>, in the customer's language, over the
///     data written down here.
/// </summary>
/// <remarks>
///     <para>
///         The layout is the template's and only the template's. What this class decides is the two
///         things a template cannot: the language — the <b>customer's</b>, frozen onto the invoice at
///         issue, never the caller's — and the vocabulary the template may write against.
///     </para>
///     <para>
///         ⚠️ Without the language the words, the dates and the amounts would be the accountant's: an
///         Italian customer would get an English invoice whenever an English-speaking accountant issued
///         it, a defect that only ever shows up in the customer's inbox. <see cref="IPdxTemplates" />
///         applies the one it is given to the translations, the <c>date</c> pipe and the
///         <c>currency</c> pipe alike.
///     </para>
/// </remarks>
public static class InvoiceDocument
{
    /// <summary>The template, embedded in this module.</summary>
    public const string Template = "invoice.pdxdoc";

    /// <summary>The document as its recipient reads it.</summary>
    /// <exception cref="InvalidOperationException">
    ///     The template named something the data does not have. It is this module's own template, so a
    ///     hole in it is a defect to stop on — an invoice is a legal document, and one with a blank where
    ///     the customer's VAT number should be is worse than none.
    /// </exception>
    public static async Task<DocumentModel> ForTheCustomerAsync(
        IPdxTemplates templates, Invoice invoice, OrganizationBillingDetailsDto issuer, CancellationToken ct = default)
    {
        Ensure.ThrowIfNull(templates);
        Ensure.ThrowIfNull(invoice);
        Ensure.ThrowIfNull(issuer);

        var document = await templates
            .DocumentAsync(Template, invoice.BilledToCulture, DataOf(invoice, issuer), ct)
            .ConfigureAwait(false);

        if (document.Warnings.Count > 0)
            throw new InvalidOperationException(
                $"{Template} asked for data the invoice does not provide: "
                + string.Join(", ", document.Warnings.Select(w => w.Path)));

        return document.Model;
    }

    /// <summary>
    ///     The roots the template writes against: <c>issuer</c>, <c>invoice</c>, <c>lines</c>.
    /// </summary>
    /// <remarks>
    ///     Dictionaries and not the entities: what the template may name is a list written here, not every
    ///     property the invoice happens to have. Amounts stay <c>Money</c>, so the <c>currency</c> pipe
    ///     writes each in its own currency the reader's way.
    /// </remarks>
    public static TemplateDataContext DataOf(Invoice invoice, OrganizationBillingDetailsDto issuer)
        => new TemplateDataContext()
            .AddSource("issuer", new Dictionary<string, object?>
            {
                ["legalName"] = issuer.LegalName,
                ["vatNumber"] = issuer.VatNumber,
                ["street"] = issuer.Address.Street,
                ["postCode"] = issuer.Address.PostCode,
                ["city"] = issuer.Address.City,
                ["country"] = issuer.Address.Country,
            })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = invoice.Number,
                ["issuedOn"] = invoice.IssuedOn,
                ["dueOn"] = invoice.DueOn,
                ["billedToName"] = invoice.BilledToName,
                ["billedToVatNumber"] = invoice.BilledToVatNumber,
                ["billedToStreet"] = invoice.BilledToAddress.Street,
                ["billedToPostCode"] = invoice.BilledToAddress.PostCode,
                ["billedToCity"] = invoice.BilledToAddress.City,
                ["billedToCountry"] = invoice.BilledToAddress.Country,
                ["net"] = invoice.NetTotal,
                ["vat"] = invoice.VatTotal,
                ["gross"] = invoice.GrossTotal,
            })
            .AddSource("lines", invoice.Lines
                .Select(line => new Dictionary<string, object?>
                {
                    ["description"] = line.Description,
                    ["quantity"] = line.Quantity,
                    ["unitPrice"] = line.UnitPrice,
                    ["vatRate"] = line.VatRate,
                    ["net"] = line.LineNet,
                    ["vat"] = line.LineVat,
                })
                .ToList());
}
