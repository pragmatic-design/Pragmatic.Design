using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Pragmatic.Composition.Hosting;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     A host that failed to start and fell back to the maintenance page has to say so, and say where.
/// </summary>
/// <remarks>
///     Measured on a consumer: the log read <c>Now listening on: http://localhost:5000</c> and
///     <c>Application started.</c> — the words of a successful start — while every request answered
///     503, on an address other than the one the host was launched with. These run a real Kestrel
///     on an address given on the command line, the way the generated entry builds the page.
/// </remarks>
public sealed class MaintenanceModeAnnouncementTests
{
    [Fact]
    public async Task Configure_WhenThePageStarts_LogsCriticalNamingTheFailureTheAddressAndTheSwitch()
    {
        var log = new CapturingLoggerProvider();
        await using var app = BuildMaintenanceApp(log, new InvalidOperationException("the schema could not be verified"));

        await app.StartAsync();
        var address = app.Urls.Single();

        var announcement = log.Entries.Where(e => e.Level == LogLevel.Critical).Select(e => e.Message).ToList();
        announcement.Should().HaveCount(1);
        announcement[0].Should().Contain("InvalidOperationException");
        announcement[0].Should().Contain("the schema could not be verified");
        announcement[0].Should().Contain(address);
        announcement[0].Should().Contain("Pragmatic:MaintenanceMode:EnableOnStartupFailure");

        await app.StopAsync();
    }

    [Fact]
    public async Task Configure_OnTheAddressFromTheCommandLine_Answers503()
    {
        var log = new CapturingLoggerProvider();
        await using var app = BuildMaintenanceApp(log, new InvalidOperationException("boom"));

        await app.StartAsync();
        using var client = new HttpClient();
        var response = await client.GetAsync(new Uri(new Uri(app.Urls.Single()), "/anything"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await app.StopAsync();
    }

    private static WebApplication BuildMaintenanceApp(CapturingLoggerProvider log, Exception startupFailure)
    {
        // Port 0: the operating system picks a free one, and the host reports it once bound.
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        var app = builder.Build();
        new MaintenanceMode(startupFailure, new MaintenanceModeOptions(), isDevelopment: false).Configure(app);
        return app;
    }
}
