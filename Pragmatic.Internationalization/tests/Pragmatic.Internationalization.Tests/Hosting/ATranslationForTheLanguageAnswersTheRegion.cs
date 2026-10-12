using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Types;

namespace Pragmatic.Internationalization.Tests.Hosting;

/// <summary>
///     A module that ships <c>translations/en.json</c> answers a host whose requests are in <c>en-US</c>.
/// </summary>
/// <remarks>
///     <para>
///         The provider the generator embeds in a module looks a culture up exactly, like the in-memory
///         one here. The host's error resolver asks the composite of all providers for the request's
///         culture, so an <c>en-US</c> request met an <c>en</c> table and found nothing: every refused
///         field answered with its key and no sentence, with no warning at build or at start. It is the
///         pair a reader picks first (<c>en.json</c>, <c>CultureCode.EnglishUS</c>).
///     </para>
///     <para>
///         Through the pipeline, not the provider: the culture comes from the middleware and the default
///         the host configures, the resolver from what <c>LocalizeProblemDetails()</c> registers.
///     </para>
/// </remarks>
[Collection("I18nContext")]
public sealed class ATranslationForTheLanguageAnswersTheRegion
{
    [Fact]
    public async Task A_request_in_en_US_reads_the_sentence_an_en_file_gives()
    {
        var messages = await RefuseAsync(
            acceptLanguage: "en-US",
            ("en", "validation.required", "This is required.")).ConfigureAwait(true);

        messages.GetProperty("title")[0].GetString().Should().Be("This is required.");
    }

    [Fact]
    public async Task A_request_with_no_language_reads_it_in_the_hosts_default_culture()
    {
        var messages = await RefuseAsync(
            acceptLanguage: null,
            ("en", "validation.required", "This is required.")).ConfigureAwait(true);

        messages.GetProperty("title")[0].GetString().Should().Be("This is required.");
    }

    /// <summary>The region's own translation, where there is one, is never overridden by its language's.</summary>
    [Fact]
    public async Task A_translation_for_the_region_wins_over_the_languages()
    {
        var messages = await RefuseAsync(
            acceptLanguage: "en-US",
            ("en", "validation.required", "This is required."),
            ("en-US", "validation.required", "This field is required.")).ConfigureAwait(true);

        messages.GetProperty("title")[0].GetString().Should().Be("This field is required.");
    }

    /// <summary>
    ///     Starts a host whose default culture is <c>en-US</c>, asks it for a refused <c>title</c> in
    ///     <paramref name="acceptLanguage" />, and returns the <c>messages</c> of the answer.
    /// </summary>
    private static async Task<JsonElement> RefuseAsync(
        string? acceptLanguage, params (string Culture, string Key, string Value)[] entries)
    {
        var translations = new InMemoryLocalizationProvider();
        foreach (var (culture, key, value) in entries)
            translations.AddString(culture, key, value);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticInternationalization()
            .DefaultCulture(CultureCode.EnglishUS)
            .Support(CultureCode.EnglishUS)
            .AddLocalizationProvider(translations)
            .LocalizeProblemDetails();

        var app = builder.Build();
        app.UsePragmaticInternationalization();
        app.MapGet("/refuse", (HttpContext context) =>
        {
            var refused = ValidationError.FromIssues([ValidationIssue.For("Title", "validation.required")]);
            var extensions = new Dictionary<string, object?>();
            ((IError)refused).WriteExtensions(extensions, context.RequestServices.GetService<IErrorMessageResolver>());
            return Results.Json(extensions);
        });

        await app.StartAsync().ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/refuse");
            if (acceptLanguage is not null)
                request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

            using var response = await app.GetTestClient().SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var json = JsonDocument.Parse(body);

            json.RootElement.TryGetProperty("messages", out var messages).Should().BeTrue(body);
            return messages.Clone();
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
