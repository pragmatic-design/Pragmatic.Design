using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Logging.Tests.CallSites;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests.AspNetCore;

/// <summary>
///     The request middleware keeps what the client sent on one line before it logs it, so its entries stay single lines in a
///     provider that writes text as it is: Microsoft's console when an application has no console of Pragmatic's, or any third
///     party's. ASP.NET decodes <c>%0D%0A</c> in a path, and a path carrying CR/LF otherwise forged a line there (CodeQL
///     <c>cs/log-forging</c>).
/// </summary>
public sealed class TheRequestIsLoggedOnOneLineTests
{
    [Fact]
    public async Task AForgedPath_IsEscaped_InEveryEntryTheMiddlewareWrites()
    {
        var written = await WhatAThirdPartyProviderReads(path: "/orders\r\nFORGED entry", throws: false);

        written.Should().HaveCount(2, "the start and the completion of the request");
        written.Should().OnlyContain(entry => !entry.Contains('\r') && !entry.Contains('\n'));
        written.Should().OnlyContain(entry => entry.Contains("/orders\\r\\nFORGED entry", StringComparison.Ordinal),
            "the value is still there, escaped");
    }

    [Fact]
    public async Task AForgedPath_IsEscaped_InTheFailedRequestsEntry()
    {
        var written = await WhatAThirdPartyProviderReads(path: "/orders\r\nFORGED entry", throws: true);

        written.Should().Contain(entry => entry.StartsWith("HTTP request failed", StringComparison.Ordinal));
        written.Should().OnlyContain(entry => !entry.Contains('\r') && !entry.Contains('\n'));
    }

    /// <summary>The control: an ordinary path is written as it is.</summary>
    [Fact]
    public async Task AnOrdinaryPath_IsWrittenAsItIs()
    {
        var written = await WhatAThirdPartyProviderReads(path: "/orders/42", throws: false);

        written.Should().OnlyContain(entry => entry.Contains("GET /orders/42", StringComparison.Ordinal));
    }

    private static async Task<List<string>> WhatAThirdPartyProviderReads(string path, bool throws)
    {
        var provider = new ThirdPartyProvider();
        using var factory = new LoggerFactory([provider]);
        var middleware = new LoggingEnrichmentMiddleware(
            _ => throws ? throw new InvalidOperationException("boom") : Task.CompletedTask,
            factory.CreateLogger<LoggingEnrichmentMiddleware>());
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;

        try
        {
            await middleware.InvokeAsync(context).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (throws)
        {
            // The middleware logs the failure and rethrows it; the entry is what is under test.
        }

        return provider.Written;
    }
}
