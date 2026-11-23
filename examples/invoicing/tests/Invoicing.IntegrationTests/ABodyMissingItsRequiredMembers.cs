using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     A request that leaves out the members an operation declares <c>required</c> is refused as the members'
///     own rule refuses an empty one: 422, one error per member, localized (#937).
/// </summary>
/// <remarks>
///     <para>
///         It answered 400 until #937, with the deserializer's text: "JSON deserialization for type
///         'Invoicing.Registry.Customers.Mutations.CreateCustomerMutationBody' was missing required
///         properties". That named a CLR type the caller never wrote, in no language of theirs, with no
///         field a form could mark. The generated body record no longer carries <c>required</c>; it checks
///         the presence of those members itself, right after the read.
///     </para>
///     <para>
///         <c>CreateCustomerMutation</c> declares three: <c>Name</c>, <c>Email</c>, <c>PreferredCulture</c>.
///         The control is the same request with the three sent empty, which the operation's validation
///         has always refused: the two answers must be the same answer.
///     </para>
/// </remarks>
public sealed class ABodyMissingItsRequiredMembers(PostgresFixture database) : InvoicingTestBase(database)
{
    private const string Customers = "api/customers";

    [Fact]
    public async Task ACustomerWithNoneOfItsThreeRequiredMembers_Answers422_OnePerMember()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var response = await accountant.PostAsJsonAsync(Customers, new { vatNumber = "IT01234567890" });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "body was: {0}", text);
        Errors(JsonDocument.Parse(text).RootElement).Should()
            .Be("email: validation.required; name: validation.required; preferredCulture: validation.required");
    }

    [Fact]
    public async Task TheAnswer_NamesNoGeneratedType()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var text = await (await accountant.PostAsJsonAsync(Customers, new { })).Content.ReadAsStringAsync();

        text.Should().NotContain("MutationBody", "the generated record is how the application is built, not the caller's contract");
        text.Should().NotContain("Invoicing.Registry", "a namespace of the application says nothing the caller can act on");
    }

    /// <summary>The control: the three members sent empty, refused by the operation's own rules.</summary>
    [Fact]
    public async Task LeftOutAndSentEmpty_AreTheSameAnswer()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var missing = await accountant.PostAsJsonAsync(Customers, new { });
        var empty = await accountant.PostAsJsonAsync(Customers, new { name = "", email = "", preferredCulture = "" });

        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        missing.StatusCode.Should().Be(empty.StatusCode);
        Errors(await ReadAsync(missing)).Should().Be(Errors(await ReadAsync(empty)));
    }

    /// <summary>
    ///     In the caller's language, as the operation's rules answer: the host localizes problem details, and
    ///     Italian is its second culture.
    /// </summary>
    [Fact]
    public async Task InItalian_TheMessagesAreItalian_AsAnEmptyMembersAre()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var missing = await ReadAsync(await PostInAsync(accountant, "it-IT", new { }));
        var empty = await ReadAsync(await PostInAsync(accountant, "it-IT", new { name = "", email = "", preferredCulture = "" }));
        var english = await ReadAsync(await PostInAsync(accountant, "en-US", new { }));

        Messages(missing).Should().Be(
            "email: Questo campo è obbligatorio.; name: Questo campo è obbligatorio.; preferredCulture: Questo campo è obbligatorio.");
        Messages(missing).Should().Be(Messages(empty));
        Messages(english).Should().Be(
            "email: This is required.; name: This is required.; preferredCulture: This is required.",
            "the control: the same request in English is not answered in Italian");
    }

    /// <summary>
    ///     A value type and a collection too: <c>CreateDraftInvoiceMutation</c> requires a <c>Guid</c> and a list, and
    ///     a <c>Guid</c> left out used to be no case at all for the operation's validator, which cannot ask a
    ///     non-nullable value whether it arrived.
    /// </summary>
    [Fact]
    public async Task ADraftWithNoCustomerAndNoLines_Answers422_NamingBoth()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var response = await accountant.PostAsJsonAsync("api/invoices", new { notes = "nothing else" });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "body was: {0}", text);
        Errors(JsonDocument.Parse(text).RootElement).Should().Be("customerId: validation.required; lines: validation.required");
    }

    private static Task<HttpResponseMessage> PostInAsync(HttpClient client, string culture, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Customers) { Content = JsonContent.Create(body) };
        request.Headers.Add("Accept-Language", culture);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The <c>errors</c> map as one line, by field: what a client matches on.</summary>
    private static string Errors(JsonElement problem) => Flatten(problem.GetProperty("errors"));

    /// <summary>The <c>messages</c> map as one line, by field: what a client shows.</summary>
    private static string Messages(JsonElement problem) => Flatten(problem.GetProperty("messages"));

    private static string Flatten(JsonElement map)
        => string.Join("; ", map.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}: {string.Join(", ", p.Value.EnumerateArray().Select(v => v.GetString()))}"));
}
