using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Humanizer;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates relative time formatting (humanizer).
/// </summary>
public static class RelativeTimeSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. RELATIVE TIME (HUMANIZER)");
        Console.WriteLine("   Human-friendly time expressions");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Setup localization provider with relative time translations
        var provider = new InMemoryLocalizationProvider()
            // English
            .AddString("en", "TimeAgo.now", "just now")
            .AddString("en", "TimeAgo.yesterday", "yesterday")
            .AddPlural("en", "TimeAgo.seconds",
                (PluralCategory.One, "{count} second ago"),
                (PluralCategory.Other, "{count} seconds ago"))
            .AddPlural("en", "TimeAgo.minutes",
                (PluralCategory.One, "{count} minute ago"),
                (PluralCategory.Other, "{count} minutes ago"))
            .AddPlural("en", "TimeAgo.hours",
                (PluralCategory.One, "{count} hour ago"),
                (PluralCategory.Other, "{count} hours ago"))
            .AddPlural("en", "TimeAgo.days",
                (PluralCategory.One, "{count} day ago"),
                (PluralCategory.Other, "{count} days ago"))
            .AddPlural("en", "TimeAgo.weeks",
                (PluralCategory.One, "{count} week ago"),
                (PluralCategory.Other, "{count} weeks ago"))
            .AddPlural("en", "TimeAgo.months",
                (PluralCategory.One, "{count} month ago"),
                (PluralCategory.Other, "{count} months ago"))
            .AddPlural("en", "TimeAgo.years",
                (PluralCategory.One, "{count} year ago"),
                (PluralCategory.Other, "{count} years ago"))
            // Italian
            .AddString("it", "TimeAgo.now", "proprio adesso")
            .AddString("it", "TimeAgo.yesterday", "ieri")
            .AddPlural("it", "TimeAgo.seconds",
                (PluralCategory.One, "{count} secondo fa"),
                (PluralCategory.Other, "{count} secondi fa"))
            .AddPlural("it", "TimeAgo.minutes",
                (PluralCategory.One, "{count} minuto fa"),
                (PluralCategory.Other, "{count} minuti fa"))
            .AddPlural("it", "TimeAgo.hours",
                (PluralCategory.One, "{count} ora fa"),
                (PluralCategory.Other, "{count} ore fa"))
            .AddPlural("it", "TimeAgo.days",
                (PluralCategory.One, "{count} giorno fa"),
                (PluralCategory.Other, "{count} giorni fa"))
            .AddPlural("it", "TimeAgo.weeks",
                (PluralCategory.One, "{count} settimana fa"),
                (PluralCategory.Other, "{count} settimane fa"))
            .AddPlural("it", "TimeAgo.months",
                (PluralCategory.One, "{count} mese fa"),
                (PluralCategory.Other, "{count} mesi fa"))
            .AddPlural("it", "TimeAgo.years",
                (PluralCategory.One, "{count} anno fa"),
                (PluralCategory.Other, "{count} anni fa"));

        var options = new I18NOptions { DefaultUICulture = CultureCode.English };

        // English relative time
        Console.WriteLine("English relative time:");
        var enLocalizer = new StringLocalizer(provider, options, "en");
        var enFormatter = new RelativeTimeFormatter(enLocalizer);

        Console.WriteLine($"  2 seconds:  {enFormatter.Format(TimeSpan.FromSeconds(2))}");
        Console.WriteLine($"  30 seconds: {enFormatter.Format(TimeSpan.FromSeconds(30))}");
        Console.WriteLine($"  5 minutes:  {enFormatter.Format(TimeSpan.FromMinutes(5))}");
        Console.WriteLine($"  3 hours:    {enFormatter.Format(TimeSpan.FromHours(3))}");
        Console.WriteLine($"  30 hours:   {enFormatter.Format(TimeSpan.FromHours(30))}");
        Console.WriteLine($"  5 days:     {enFormatter.Format(TimeSpan.FromDays(5))}");
        Console.WriteLine($"  2 weeks:    {enFormatter.Format(TimeSpan.FromDays(14))}");
        Console.WriteLine($"  3 months:   {enFormatter.Format(TimeSpan.FromDays(90))}");
        Console.WriteLine($"  2 years:    {enFormatter.Format(TimeSpan.FromDays(730))}");
        Console.WriteLine();

        // Italian relative time
        Console.WriteLine("Italian relative time:");
        var itLocalizer = new StringLocalizer(provider, options, "it");
        var itFormatter = new RelativeTimeFormatter(itLocalizer);

        Console.WriteLine($"  proprio adesso: {itFormatter.Format(TimeSpan.FromSeconds(2))}");
        Console.WriteLine($"  5 minuti:       {itFormatter.Format(TimeSpan.FromMinutes(5))}");
        Console.WriteLine($"  3 ore:          {itFormatter.Format(TimeSpan.FromHours(3))}");
        Console.WriteLine($"  ieri:           {itFormatter.Format(TimeSpan.FromHours(30))}");
        Console.WriteLine($"  5 giorni:       {itFormatter.Format(TimeSpan.FromDays(5))}");
        Console.WriteLine();

        // Using with DateTimeOffset
        Console.WriteLine("Using with DateTimeOffset:");
        var now = DateTimeOffset.UtcNow;
        Console.WriteLine($"  3 hours ago:  {enFormatter.Format(now.AddHours(-3))}");
        Console.WriteLine($"  2 days ago:   {enFormatter.Format(now.AddDays(-2))}");
        Console.WriteLine();

        // Comparing two dates
        Console.WriteLine("Comparing two dates:");
        var start = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 3, 15, 0, 0, TimeSpan.Zero);
        Console.WriteLine($"  Jan 1 10:00 to Jan 3 15:00: {enFormatter.Format(start, end)}");
        Console.WriteLine();
    }
}