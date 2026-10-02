using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     A culture the host declares and nobody translated is said once, when the provider loads.
/// </summary>
/// <remarks>
///     <para>
///         A missing translation file is a deployment mistake, and the only moment it is cheap to
///         notice is the moment the provider reads the directory. Noticed per lookup instead, it is a
///         <c>Debug</c> line per request, below every default log level, and a caller reading the
///         default language forever.
///     </para>
///     <para>
///         ⚠️ It is a warning and not a failure: a host that supports four languages and ships three is
///         wrong about one of them, not unable to start, and refusing to boot over a translation file
///         would be worse than saying so.
///     </para>
/// </remarks>
public sealed class AConfiguredCultureWithNoFileIsReportedTests : IDisposable
{
    private readonly DirectoryInfo _translations = Directory.CreateTempSubdirectory("pragmatic-i18n-");

    private void Write(string culture)
        => File.WriteAllText(Path.Combine(_translations.FullName, $"{culture}.json"), """{"greeting":"Hello"}""");

    public void Dispose() => _translations.Delete(recursive: true);

    /// <summary>Builds the provider the way an application does, and returns what was logged.</summary>
    private IReadOnlyList<string> WarningsFor(params CultureCode[] supported)
    {
        var recorder = new Recorder();

        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        services.AddPragmaticInternationalization()
            .Support(supported)
            .AddJsonTranslations(_translations.FullName);

        // Resolving is what constructs it, and the report belongs to construction rather than to a
        // startup step somebody has to remember to add.
        using var provider = services.BuildServiceProvider();
        _ = provider.GetServices<ILocalizationProvider>().ToList();

        return recorder.Warnings;
    }

    [Fact]
    public void ACultureWithNoFileAnywhereInItsChain_IsReported()
    {
        Write("en");

        WarningsFor(CultureCode.EnglishUS, CultureCode.German)
            .Should().Contain(w => w.Contains("de", StringComparison.Ordinal),
                "German is declared and nothing translates it, which is a deployment mistake");
    }

    /// <summary>
    ///     The control: a culture that resolves through its language is not reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "a missing culture is reported" is satisfied by reporting every culture whose
    ///     name is not exactly a file name — which is every region in every application that ships one
    ///     file per language, and a warning everybody learns to ignore.
    /// </remarks>
    [Fact]
    public void ACultureThatResolvesThroughItsLanguage_IsNotReported()
    {
        Write("en");

        WarningsFor(CultureCode.EnglishUS)
            .Should().BeEmpty("en-US reads en.json, so nothing is missing");
    }

    /// <summary>Records what was logged at warning level, and nothing else.</summary>
    private sealed class Recorder : ILoggerProvider, ILogger
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}
