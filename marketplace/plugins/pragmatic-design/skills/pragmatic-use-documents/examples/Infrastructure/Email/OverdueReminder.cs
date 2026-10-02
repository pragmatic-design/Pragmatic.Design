using Invoicing.Registry.Dtos;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Email;
using Pragmatic.Email.Builder;

namespace Invoicing.Billing.Infrastructure.Email;

/// <summary>
///     The message a customer gets when an invoice of theirs is past due:
///     <c>templates/overdue-reminder.pdxemail</c>, in the customer's language.
/// </summary>
/// <remarks>
///     <para>
///         The sender is the company's own address, never the application's: the customer knows who
///         <em>Acme</em> is and has never heard of this service. That is why the host leaves
///         <c>DefaultFrom</c> unset — a default here would quietly send every company's mail from the same
///         address.
///     </para>
///     <para>
///         ⚠️ The language is the <b>customer's</b>, frozen onto the invoice. A job has no request and
///         therefore no <c>Accept-Language</c>: taken from the ambient culture, every reminder would go out
///         in the default language, to everybody, and the only place that shows is a customer's inbox.
///     </para>
///     <para>
///         The customer's name reaches the HTML encoded, because it is a value in the data and not markup
///         in the template. Interpolated into an HTML string, a company called
///         <c>&lt;b&gt;Acme&lt;/b&gt;</c> would change the mail.
///     </para>
/// </remarks>
internal static class OverdueReminder
{
    public const string Template = "overdue-reminder.pdxemail";

    public static async Task<EmailMessage> BuildAsync(
        IPdxTemplates templates, OrganizationBillingDetailsDto issuer, Invoice invoice, byte[] document,
        CancellationToken ct = default)
    {
        var mail = await templates
            .EmailAsync(Template, invoice.BilledToCulture, DataOf(issuer, invoice), ct)
            .ConfigureAwait(false);

        return new EmailMessageBuilder()
            .From(issuer.SenderEmail, issuer.LegalName)
            .To(invoice.BilledToEmail, invoice.BilledToName)
            .Subject(mail.Subject)
            .TextBody(mail.Text)
            .HtmlBody(mail.Html)
            // The document that was issued, read from storage — never rendered again. A second rendering
            // would be a second document for one invoice, and the customer would hold two.
            .Attach($"{(invoice.Number ?? "").Replace('/', '-')}.pdf", document, "application/pdf")
            .Build();
    }

    private static TemplateDataContext DataOf(OrganizationBillingDetailsDto issuer, Invoice invoice)
        => new TemplateDataContext()
            .AddSource("issuer", new Dictionary<string, object?> { ["legalName"] = issuer.LegalName })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = invoice.Number,
                ["billedToName"] = invoice.BilledToName,
                ["dueOn"] = invoice.DueOn,
                ["outstanding"] = invoice.GrossTotal - invoice.AmountPaid,
            });
}
