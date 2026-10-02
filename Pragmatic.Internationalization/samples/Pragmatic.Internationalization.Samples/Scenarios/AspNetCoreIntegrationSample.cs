using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Internationalization.AspNetCore.Endpoints;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Formatting;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates the ASP.NET Core integration end to end against a real Kestrel host:
///     <c>AddPragmaticInternationalization</c> with an
///     in-memory localization provider, the <c>UsePragmaticInternationalization</c>
///     culture-resolution middleware, the DI-injectable <see cref="GlobalizationFormatter"/>,
///     and the <c>MapPragmaticTranslations</c> translation API. Endpoints are exercised over HTTP.
/// </summary>
public static class AspNetCoreIntegrationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("ASP.NET CORE INTEGRATION");
        Console.WriteLine("   AddPragmaticInternationalization + middleware + formatter + endpoints");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:5199");

        // Seed an in-memory translation catalog and register it as a localization provider.
        // AddLocalizationProvider also wires the CompositeLocalizationProvider the
        // translation endpoint reads from.
        var translations = new InMemoryLocalizationProvider()
            .AddString("en-US", "greeting", "Hello")
            .AddString("it-IT", "greeting", "Ciao");

        var i18n = builder.Services.AddPragmaticInternationalization(options =>
        {
            options.DefaultUICulture = CultureCode.EnglishUS;
            options.SupportedCultures = [CultureCode.EnglishUS, CultureCode.Italian];
        });
        i18n.AddLocalizationProvider(translations);

        await using var app = builder.Build();

        // Culture-resolution middleware (route > query > header > cookie > default).
        app.UsePragmaticInternationalization();

        // A formatting endpoint resolving the per-request culture via GlobalizationFormatter.
        app.MapGet("/price", (GlobalizationFormatter formatter) =>
        {
            var formatted = formatter.FormatMoney(1234.56m, CurrencyCode.FromCode("EUR"));
            return Results.Ok(new { culture = I18NContext.Current.CultureCode, price = formatted });
        });

        // The shipped translation API: GET /api/i18n/{culture}.
        app.MapPragmaticTranslations();

        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5199") };

        // ?culture= drives the middleware-resolved culture for the request.
        var enPrice = await client.GetStringAsync("/price?culture=en-US");
        Console.WriteLine($"  GET /price?culture=en-US     -> {enPrice}");

        var itPrice = await client.GetStringAsync("/price?culture=it-IT");
        Console.WriteLine($"  GET /price?culture=it-IT     -> {itPrice}");

        var enTranslations = await client.GetStringAsync("/api/i18n/en-US");
        Console.WriteLine($"  GET /api/i18n/en-US          -> {enTranslations}");

        var itTranslations = await client.GetStringAsync("/api/i18n/it-IT");
        Console.WriteLine($"  GET /api/i18n/it-IT          -> {itTranslations}");

        await app.StopAsync();
        Console.WriteLine();
    }
}
