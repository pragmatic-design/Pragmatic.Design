using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.Context;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using TimeOff.Leave.Errors;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     The application answers in the user's language, Italian or English: from the request
///     when it says, and from the profile when it does not.
/// </summary>
/// <remarks>
///     What a client matches on — the code, the field, the message key — is the same in both. What a
///     person reads — the title, the detail, the message beside each field — is in their language.
/// </remarks>
public sealed class SpeakingTheUsersLanguage(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task TheSameInvalidRequest_HasTheSameCodesAndFields_AndMessagesInTheLanguageAsked()
    {
        var (employee, sick) = await AnEmployeeAsync();

        var english = await ProblemAsync(await employee.SendAsync(EndsBeforeItStarts(sick, "en-US")));
        var italian = await ProblemAsync(await employee.SendAsync(EndsBeforeItStarts(sick, "it-IT")));

        foreach (var problem in new[] { english, italian })
        {
            problem.GetProperty("status").GetInt32().Should().Be(422);
            problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
            problem.GetProperty("errors").GetProperty("to")[0].GetString()
                .Should().Be("validation.leave_request.ends_before_it_starts");
        }

        english.GetProperty("messages").GetProperty("to")[0].GetString().Should().Be("The leave ends before it starts.");
        italian.GetProperty("messages").GetProperty("to")[0].GetString().Should().Be("Il periodo finisce prima di cominciare.");
        english.GetProperty("title").GetString().Should().Be("The request is not valid");
        italian.GetProperty("title").GetString().Should().Be("La richiesta non è valida");
    }

    /// <summary>A refusal of the domain speaks the language too, with the numbers in their place.</summary>
    [Fact]
    public async Task ATypedError_HasTheSameCode_AndItsWordsInTheLanguageAsked()
    {
        var (employee, _) = await AnEmployeeAsync();
        var vacation = await DefineKindAsync();

        var english = await ProblemAsync(await employee.SendAsync(Asking(vacation, Day(0), Day(2), "en-US")));
        var italian = await ProblemAsync(await employee.SendAsync(Asking(vacation, Day(0), Day(2), "it-IT")));

        english.GetProperty("code").GetString().Should().Be("ALLOWANCE_EXCEEDED");
        italian.GetProperty("code").GetString().Should().Be("ALLOWANCE_EXCEEDED");
        english.GetProperty("detail").GetString()
            .Should().Be("The request takes 3, and 0 is left of your allowance for the year.");
        italian.GetProperty("detail").GetString()
            .Should().Be("La richiesta prende 3 e del tuo monte per l'anno resta 0.");
    }

    /// <summary>
    ///     A request that names no language is answered in the one the profile chose; one that names a
    ///     language is answered in that one — the request is the more specific of the two.
    /// </summary>
    [Fact]
    public async Task WithoutALanguageInTheRequest_TheProfileDecides()
    {
        var (employee, sick) = await AnEmployeeAsync();
        await ReadJsonAsync(await employee.PutAsJsonAsync("/api/me/language", new { language = "it-IT" }));

        var unsaid = await ProblemAsync(await employee.SendAsync(EndsBeforeItStarts(sick, language: null)));
        var said = await ProblemAsync(await employee.SendAsync(EndsBeforeItStarts(sick, "en-US")));

        unsaid.GetProperty("messages").GetProperty("to")[0].GetString().Should().Be("Il periodo finisce prima di cominciare.");
        said.GetProperty("messages").GetProperty("to")[0].GetString().Should().Be("The leave ends before it starts.");
        (await ReadJsonAsync(await employee.GetAsync("/api/me"))).GetProperty("preferredCulture").GetString()
            .Should().Be("it-IT");
    }

    /// <summary>The control: without a profile or a language asked, the default — English.</summary>
    [Fact]
    public async Task WithNeitherProfileNorRequest_TheDefaultLanguage()
    {
        var (employee, sick) = await AnEmployeeAsync();

        var problem = await ProblemAsync(await employee.SendAsync(EndsBeforeItStarts(sick, language: null)));

        problem.GetProperty("messages").GetProperty("to")[0].GetString().Should().Be("The leave ends before it starts.");
    }

    [Fact]
    public async Task ALanguageTheApplicationDoesNotSpeak_IsRefused()
    {
        var (employee, _) = await AnEmployeeAsync();

        var response = await employee.PutAsJsonAsync("/api/me/language", new { language = "fr-FR" });

        var problem = await ProblemAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(422);
        problem.GetProperty("errors").GetProperty("preferredCulture")[0].GetString()
            .Should().Be("validation.employee.unsupported_language");
    }

    /// <summary>
    ///     Every refusal of the example has a title and a detail in both languages. The translation
    ///     files are checked against each other when the module compiles; this checks them against
    ///     the errors.
    /// </summary>
    [Fact]
    public void EveryErrorOfTheExample_HasItsWordsInBothLanguages()
    {
        var resolver = Services.GetRequiredService<IErrorMessageResolver>();
        Error[] errors =
        [
            new OverlappingLeaveRequestError(), new AllowanceExceededError(), new CannotDecideOwnRequestError(),
            new NotYourRequestError(), new LeaveAlreadyStartedError()
        ];

        foreach (var language in new[] { "en-US", "it-IT" })
        {
            I18NContext.WithCulture(language, () =>
            {
                foreach (var error in errors)
                {
                    resolver.ResolveTitle(error.Code, error).Should().NotBeNullOrEmpty($"{error.Code} in {language}");
                    resolver.Resolve(error.Code, error).Should().NotBeNullOrEmpty($"{error.Code} in {language}");
                }
            });
        }
    }

    private static HttpRequestMessage EndsBeforeItStarts(Guid kind, string? language)
        => Asking(kind, Day(2), Day(0), language);

    private static HttpRequestMessage Asking(Guid kind, string from, string to, string? language)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/leave-requests")
        {
            Content = JsonContent.Create(new { absenceKindId = kind, from, to })
        };
        if (language is not null)
            request.Headers.AcceptLanguage.ParseAdd(language);
        return request;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeFalse(body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<(HttpClient Employee, Guid SickLeave)> AnEmployeeAsync()
    {
        var sick = await DefineKindAsync(usesAllowance: false);
        var employee = await HireAsync();
        return (await SignInAsync(employee.Account), sick);
    }
}
