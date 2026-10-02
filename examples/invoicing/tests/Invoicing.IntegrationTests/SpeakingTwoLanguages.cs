using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.Billing.Entities;
using Invoicing.Billing.Infrastructure.Jobs;
using Invoicing.Billing.Invoices;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry.Contracts;
using Invoicing.Registry.Organizations.Queries;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Model;
using Pragmatic.Email.Testing;
using Pragmatic.Documents.Markup;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     Two languages, and the distinction the framework has an API for: the language of an
///     <b>answer</b> is the caller's, the language of a <b>document</b> is the recipient's.
/// </summary>
/// <remarks>
///     Every message a user of this application reads is a key, an <c>[Invariant("…")]</c>'s sentence
///     included: it reaches the caller as the title of <c>InvariantViolationError</c>, and the rule
///     names its own key. One key shared by every invariant in the application would lose
///     <em>which</em> rule refused; <see cref="TheRefusalOfAnAggregateRule_IsInTheLanguageTheRequestAsksFor" />
///     reads the rule's own.
/// </remarks>
public sealed class SpeakingTwoLanguages(PostgresFixture database) : InvoicingTestBase(database)
{
    private const string Italian = "it-IT";
    private const string English = "en-US";

    private static readonly DateTimeOffset Issued = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Issued);
    private InMemoryTransport _mailbox = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock>(_clock);
        _mailbox = services.AddEmailTestHarness();
    }

    /// <summary>
    ///     The same invalid request, twice, in two languages: two messages, one meaning.
    /// </summary>
    [Fact]
    public async Task TheRefusalIsInTheLanguageTheRequestAsksFor()
    {
        var tenant = await OnboardAsync();

        var italian = await ARefusedCustomerAsync(tenant, Italian);
        var english = await ARefusedCustomerAsync(tenant, English);

        italian.Should().Contain("La richiesta non è valida", "the title of the refusal")
            .And.Contain("Questo non è un indirizzo email valido.", "and the message beside the field");
        english.Should().Contain("The request is not valid")
            .And.Contain("This is not a valid email address.");
        StatusOf(italian).Should().Be(StatusOf(english), "the same refusal, said in two languages");
    }

    /// <summary>
    ///     The refusal of an <b>aggregate rule</b>, in the caller's language too.
    /// </summary>
    /// <remarks>
    ///     The rule lives on <c>InvoiceLine</c> and is checked when the invoice that carries the line is
    ///     written. What is asserted here is the key: the line at 13 % is refused with the
    ///     Italian sentence, and the English caller reads the English one — where before, whatever the
    ///     request asked for, both read the sentence written in the attribute.
    /// </remarks>
    [Fact]
    public async Task TheRefusalOfAnAggregateRule_IsInTheLanguageTheRequestAsksFor()
    {
        var tenant = await OnboardAsync();

        var italian = await ARefusedInvoiceAsync(tenant, Italian);
        var english = await ARefusedInvoiceAsync(tenant, English);

        italian.Should().Contain("Aliquota IVA non prevista", "the title of the refusal")
            .And.Contain("Le aliquote ammesse sono 0, 4, 5, 10 e 22 per cento.", "and what it explains");
        english.Should().Contain("VAT rate not in use")
            .And.Contain("The rates in use are 0, 4, 5, 10 and 22 per cent.");
        StatusOf(italian).Should().Be(422, "an aggregate rule refuses what it cannot accept");
        StatusOf(english).Should().Be(StatusOf(italian), "the same refusal, said in two languages");
    }

    [Fact]
    public async Task AnUnsupportedLanguage_FallsBackToTheDefault()
    {
        var tenant = await OnboardAsync();

        var german = await ARefusedCustomerAsync(tenant, "de-DE");

        german.Should().Contain("The request is not valid",
            "a language the application does not speak is answered in the default one");
    }

    /// <summary>
    ///     The invoice of an Italian customer, issued by a caller working in English, is in Italian.
    /// </summary>
    /// <remarks>
    ///     Asserted on the <see cref="DocumentModel" /> that <see cref="InvoiceDocument.ForTheCustomerAsync" />
    ///     builds — the method the issue action calls — and not on the stored bytes: a PDF's text lives in
    ///     a compressed stream, so searching the file for a word finds nothing whether or not it is there.
    ///     The ambient culture of this test is the default, English, which is what makes the
    ///     assertion mean something: pass the ambient culture instead of the customer's and it goes red.
    /// </remarks>
    [Fact]
    public async Task ThePdfIsInTheCustomersLanguage_NotTheIssuers()
    {
        var company = await ACompanyAsync(Italian);
        var invoice = await company.IssueAsync(asking: English);

        var document = Words(await company.DocumentOfAsync(this, invoice));

        document.Should().Contain("Fattura").And.Contain("Intestata a").And.Contain("Totale");
        document.Should().NotContain("Billed to", "the accountant's language is not the customer's");
    }

    /// <summary>
    ///     Two customers of one company, in two languages, one sweep: two reminders, each in its own.
    ///     This is the assertion that fails when the job has no culture scope — a job has no request, so
    ///     nothing would set the language and both would go out in the default one.
    /// </summary>
    [Fact]
    public async Task TheReminderIsInTheCustomersLanguage()
    {
        var company = await ACompanyAsync(Italian);
        var reader = await company.AnotherCustomerAsync(English);
        await company.IssueAsync();
        await company.IssueAsync(customer: reader);

        _clock.SetDate(2026, 4, 20);
        await SweepAsync(company.Tenant, new DateOnly(2026, 4, 20));

        SubjectOf(company.EmailOf(Italian)).Should().StartWith("La fattura ").And.EndWith(" è scaduta");
        SubjectOf(company.EmailOf(English)).Should().StartWith("Invoice ").And.EndWith(" is overdue");
    }

    /// <summary>
    ///     A customer's name is text in the reminder, never markup.
    /// </summary>
    /// <remarks>
    ///     An HTML string with the name interpolated into it would let a company called
    ///     <c>&lt;b&gt;Bold&lt;/b&gt;</c> change the mail its customers receive. From the template the name
    ///     is a value, and the renderer encodes values.
    /// </remarks>
    [Fact]
    public async Task TheCustomersName_IsTextInTheReminder_NotMarkup()
    {
        var company = await ACompanyAsync(English);
        var marked = await company.AnotherCustomerAsync(Italian, name: "<b>Bold</b> S.r.l.");
        await company.IssueAsync(customer: marked);

        _clock.SetDate(2026, 4, 20);
        await SweepAsync(company.Tenant, new DateOnly(2026, 4, 20));

        var html = _mailbox.SentWhere(m => m.To.Any(a => a.Address == company.EmailOf(Italian))).Single().Message.HtmlBody;
        html.Should().Contain("&lt;b&gt;Bold&lt;/b&gt;").And.NotContain("<b>Bold</b>");
    }

    /// <summary>
    ///     The amounts and the dates follow the culture in force, not an invariant default.
    /// </summary>
    [Fact]
    public async Task AmountsAndDatesFollowTheCulture()
    {
        var italian = await ACompanyAsync(Italian);
        var english = await ACompanyAsync(English, "globex");
        var theirs = await italian.IssueAsync();
        var ours = await english.IssueAsync();

        var inItalian = Words(await italian.DocumentOfAsync(this, theirs));
        var inEnglish = Words(await english.DocumentOfAsync(this, ours));

        inItalian.Should().Contain("610,00").And.Contain("13/04/2026");
        inEnglish.Should().Contain("610.00").And.Contain("4/13/2026");
    }

    /// <summary>
    ///     The control that does not depend on anyone remembering: both modules' files hold the same keys.
    /// </summary>
    /// <remarks>
    ///     The build already refuses a key one language has and the other lacks (PRAG1802), so this asserts
    ///     what the build cannot see — that the files the <em>running</em> application reads are the ones
    ///     that were compiled, each in its own folder. Two modules copying <c>translations/en-US.json</c>
    ///     to one directory silently leaves only one of them, and every message of the other resolves to
    ///     its own key.
    /// </remarks>
    [Theory]
    [InlineData("registry")]
    [InlineData("billing")]
    public void EveryKeyExistsInBothLanguages(string module)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "translations", module);

        var english = KeysOf(Path.Combine(folder, "en-US.json"));
        var italian = KeysOf(Path.Combine(folder, "it-IT.json"));

        english.Should().NotBeEmpty($"{module} ships its messages beside the application");
        italian.Except(english).Should().BeEmpty("keys only Italian has");
        english.Except(italian).Should().BeEmpty("keys only English has");
    }

    private static IReadOnlyCollection<string> KeysOf(string file)
        => File.Exists(file)
            ? [.. JsonDocument.Parse(File.ReadAllText(file)).RootElement.EnumerateObject().Select(p => p.Name)]
            : [];

    /// <summary>Every word of a document model, as one string: the model is what the renderer is given.</summary>
    private static string Words(DocumentModel document) => JsonSerializer.Serialize(document);

    private string SubjectOf(string address)
        => _mailbox.SentWhere(m => m.To.Any(a => a.Address == address)).Single().Message.Subject;

    /// <summary>The body of a refused create, as text, in the language the request asked for.</summary>
    /// <remarks>
    ///     The email is the invalid part, and every other required member is there on purpose: a member
    ///     left out fails deserialization and answers a framework <c>400 Bad Request</c> that never reaches
    ///     the validation pipeline — which is what the first version of this test measured, and it measured
    ///     the wrong thing.
    /// </remarks>
    private async Task<string> ARefusedCustomerAsync(string tenant, string language)
    {
        var client = As(TestUsers.Accountant, tenant);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await client.PostAsJsonAsync("api/customers", new
        {
            name = "A customer",
            vatNumber = "IT12345678901",
            email = "not-an-email-address",
            preferredCulture = Italian,
        });

        response.IsSuccessStatusCode.Should().BeFalse("the email is not an address");

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The body of an invoice refused by an aggregate rule, in the language the request asked for.</summary>
    /// <remarks>
    ///     A line at 13 %: the rate is not one of the five in use, and the rule that says so lives on the
    ///     line. Everything else about the request is valid on purpose — a payload the validators refuse
    ///     never reaches the invariants, and this has to measure the invariant's refusal.
    /// </remarks>
    private async Task<string> ARefusedInvoiceAsync(string tenant, string language)
    {
        var client = As(TestUsers.Accountant, tenant);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var customerId = await new Company(client, Guid.Empty, tenant, language).AnotherCustomerAsync(language);

        var response = await client.PostAsJsonAsync("api/invoices", new
        {
            customerId,
            lines = new object[]
            {
                new
                {
                    description = "At a rate nobody charges",
                    quantity = 1m,
                    unitPrice = new { amount = 10m, currency = "EUR" },
                    vatRate = 13m
                }
            }
        });

        response.IsSuccessStatusCode.Should().BeFalse("13 % is not a rate in use");

        return await response.Content.ReadAsStringAsync();
    }

    private static int StatusOf(string problemDetails)
        => JsonDocument.Parse(problemDetails).RootElement.GetProperty("status").GetInt32();

    private async Task SweepAsync(string tenant, DateOnly today)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var swept = await services.ServiceProvider.GetRequiredService<IOverdueReminderSweep>().RunAsync(today);
        swept.IsSuccess.Should().BeTrue();
    }

    private async Task<Company> ACompanyAsync(string culture, string name = "acme")
    {
        var tenant = await OnboardAsync(name);
        var company = new Company(As(TestUsers.Accountant, tenant), Guid.Empty, tenant, culture);

        return company with { CustomerId = await company.AnotherCustomerAsync(culture) };
    }

    /// <summary>One company, and the customers whose language its documents are written in.</summary>
    private sealed record Company(HttpClient Accountant, Guid CustomerId, string Tenant, string Culture)
    {
        public string CustomerEmail => EmailOf(Culture);

        /// <summary>
        ///     The address of this company's customer who reads <paramref name="culture" />: the language is
        ///     in the address so the mailbox can be asked about it by name.
        /// </summary>
        public string EmailOf(string culture) => $"billing+{culture}@{Tenant}.test";

        public async Task<Guid> AnotherCustomerAsync(string culture, string? name = null)
        {
            var customer = await ReadJsonAsync(await Accountant.PostAsJsonAsync("api/customers", new
            {
                name = name ?? $"Customer {culture}",
                vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
                email = EmailOf(culture),
                preferredCulture = culture,
                paymentTermsDays = 30,
                addressStreet = "Via Roma 1",
                addressPostCode = "20121",
                addressCity = "Milano",
                addressCountry = "IT",
            }));

            return customer.GetProperty("id").GetGuid();
        }

        /// <summary>An issued invoice of 610,00 gross, asked for in <paramref name="asking" />.</summary>
        public async Task<Guid> IssueAsync(Guid? customer = null, string? asking = null)
        {
            var client = Accountant;
            if (asking is not null)
            {
                client.DefaultRequestHeaders.AcceptLanguage.Clear();
                client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(asking));
            }

            var draft = await ReadJsonAsync(await client.PostAsJsonAsync("api/invoices", new
            {
                customerId = customer ?? CustomerId,
                lines = new object[]
                {
                    new { description = "Consulting", quantity = 2m, unitPrice = new { amount = 250m, currency = "EUR" }, vatRate = 22m },
                },
            }));

            var id = draft.GetProperty("id").GetGuid();
            await ReadSuccessAsync(await client.PostAsync($"api/invoices/{id}/issue", null));

            return id;
        }

        /// <summary>
        ///     The document of a stored invoice, built the way the issue action builds it — scope included.
        /// </summary>
        public async Task<DocumentModel> DocumentOfAsync(SpeakingTwoLanguages test, Guid invoiceId)
        {
            using var tenantScope = TenantScope.BeginScope(Tenant);
            await using var scope = test.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

            var invoice = await scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice>>()
                .GetByIdAsync(invoiceId);
            var issuer = await scope.ServiceProvider.GetRequiredService<IRegistryReads>()
                .GetOrganizationBillingDetails(new GetOrganizationBillingDetailsQuery { Slug = Tenant });

            return await InvoiceDocument.ForTheCustomerAsync(
                scope.ServiceProvider.GetRequiredService<IPdxTemplates>(), invoice!, issuer[0]);
        }
    }
}
