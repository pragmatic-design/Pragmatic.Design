using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Composition.Remote;
using Pragmatic.Endpoints.Responses;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     Full remote round-trip for a file-returning action, over real Kestrel sockets: the generated
///     invoker POSTs to <c>/_pragmatic/invoke</c>, the dispatcher streams the bytes back instead of the
///     JSON envelope, and the invoker rebuilds a <see cref="FileResponse" /> from the response.
/// </summary>
/// <remarks>
///     <para>
///         Two apps, because one is not enough to catch what actually breaks. The <i>boundary</i> app
///         serves <c>/_pragmatic/invoke</c>; the <i>host</i> app is the public face, and it is on the way
///         back out — where the host renders a stream it did not produce onto its own request — that a
///         download turns into wrong bytes without anything throwing.
///     </para>
///     <para>
///         The client half mirrors the code the source generator emits
///         (<c>BoundaryInterfaceTemplate.Remote</c>) — including the part that is easy to get wrong: the
///         <see cref="HttpResponseMessage" /> is NOT disposed when the invoker returns, because its
///         content stream is what the caller reads. Over a real connection that mistake is not
///         theoretical: a disposed response tears the socket down and the caller reads nothing.
///     </para>
/// </remarks>
public class RemoteFileInvokeRoundTripTests : IAsyncLifetime
{
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("%PDF-1.7 pretend bytes ", 4096)));

    private const string ETagValue = "v7";

    private WebApplication _boundaryApp = null!;
    private HttpClient _boundaryClient = null!;
    private WebApplication _hostApp = null!;
    private HttpClient _hostClient = null!;

    public sealed class DownloadInvoicePdfAction : DomainAction<FileResponse>
    {
        public string InvoiceNumber { get; init; } = "";

        public override Task<Result<FileResponse, IError>> Execute(CancellationToken ct = default)
            => throw new NotSupportedException("dispatched remotely in this test");
    }

    private sealed class ServerDownloadInvoker : IDomainActionInvoker<DownloadInvoicePdfAction, FileResponse>
    {
        public Task<Result<FileResponse, IError>> InvokeAsync(
            DownloadInvoicePdfAction action, CancellationToken ct = default)
        {
            if (action.InvoiceNumber == "missing")
            {
                return Task.FromResult(Result<FileResponse, IError>.Failure(
                    new RemoteError { Code = "NOT_FOUND", StatusCode = 404, Title = "Invoice not found" }));
            }

            var inline = action.InvoiceNumber.StartsWith("inline", StringComparison.Ordinal);

            return Task.FromResult(Result<FileResponse, IError>.Success(
                new FileResponse(
                    new MemoryStream(Pdf), "application/pdf", $"{action.InvoiceNumber}.pdf",
                    EnableRangeProcessing: true)
                {
                    ETag = ETagValue,
                    Inline = inline
                }));
        }
    }

    public async Task InitializeAsync()
    {
        // ── The remote boundary: exactly what PragmaticInvokeEndpointTemplate generates ──
        _boundaryApp = WebApplication.CreateSlimBuilder().Build();
        _boundaryApp.Urls.Add("http://127.0.0.1:0");

        _boundaryApp.MapPost("/_pragmatic/invoke", async (HttpContext ctx) =>
        {
            var request = await ctx.Request.ReadFromJsonAsync<PragmaticInvokeRequest>(ctx.RequestAborted)
                .ConfigureAwait(false);
            var action = request!.Payload.Deserialize<DownloadInvoicePdfAction>()!;

            return await PragmaticInvokeDispatcher.InvokeFileActionAsync(
                    action,
                    new ServerDownloadInvoker(),
                    static file => Pragmatic.Endpoints.Extensions.FileResponseExtensions.ToResult(
                        file, null, includeRpcMetadata: true),
                    ctx.RequestAborted)
                .ConfigureAwait(false);
        });

        await _boundaryApp.StartAsync().ConfigureAwait(false);
        var boundaryAddress = AddressOf(_boundaryApp);
        _boundaryClient = new HttpClient { BaseAddress = new Uri(boundaryAddress) };

        // ── The host: public endpoint, remote invoker, generated wiring ──
        var hostBuilder = WebApplication.CreateSlimBuilder();
        hostBuilder.Services.AddHttpContextAccessor();
        hostBuilder.Services.AddTransient<PragmaticRemoteRangeHandler>();
        hostBuilder.Services
            .AddHttpClient("Pragmatic.Remote.Billing", c => c.BaseAddress = new Uri(boundaryAddress))
            .AddHttpMessageHandler<PragmaticRemoteRangeHandler>();

        _hostApp = hostBuilder.Build();
        _hostApp.Urls.Add("http://127.0.0.1:0");

        // What the generated endpoint handler does for a DomainAction<FileResponse>.
        _hostApp.MapGet("/invoices/{invoiceNumber}/download",
            async (string invoiceNumber, IHttpClientFactory factory, HttpContext ctx) =>
            {
                var client = factory.CreateClient("Pragmatic.Remote.Billing");
                var result = await InvokeAsync(client, invoiceNumber).ConfigureAwait(false);

                return result.Match(
                    file => Pragmatic.Endpoints.Extensions.FileResponseExtensions.ToResult(file, ctx),
                    (IError error) => Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(error));
            });

        await _hostApp.StartAsync().ConfigureAwait(false);
        _hostClient = new HttpClient { BaseAddress = new Uri(AddressOf(_hostApp)) };
    }

    private static string AddressOf(WebApplication app)
        => app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

    public async Task DisposeAsync()
    {
        _hostClient.Dispose();
        _boundaryClient.Dispose();
        await _hostApp.DisposeAsync().ConfigureAwait(false);
        await _boundaryApp.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The generated remote invoker, transcribed.</summary>
    private static async Task<Result<FileResponse, IError>> InvokeAsync(HttpClient client, string invoiceNumber)
    {
        var payload = JsonSerializer.SerializeToElement(new DownloadInvoicePdfAction { InvoiceNumber = invoiceNumber });
        var requestBody = new { actionType = typeof(DownloadInvoicePdfAction).FullName, payload };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/_pragmatic/invoke")
        {
            Content = JsonContent.Create(requestBody),
        };

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var json = default(JsonElement);
            try
            {
                json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
            }

            var errorCode = "REMOTE_ERROR";
            var errorTitle = "Remote invocation failed";
            var errorStatus = (int)response.StatusCode;
            if (json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("error", out var errorProp) &&
                errorProp.ValueKind == JsonValueKind.Object)
            {
                if (errorProp.TryGetProperty("title", out var t)) errorTitle = t.GetString() ?? errorTitle;
                if (errorProp.TryGetProperty("status", out var s)) errorStatus = s.GetInt32();
                if (errorProp.TryGetProperty("code", out var c)) errorCode = c.GetString() ?? errorCode;
            }

            response.Dispose();
            return Result<FileResponse, IError>.Failure(new RemoteError
            {
                Code = errorCode,
                StatusCode = errorStatus,
                Title = errorTitle
            });
        }

        var file = await RemoteFileResponse.ReadAsync(response, ct: CancellationToken.None).ConfigureAwait(false);
        return Result<FileResponse, IError>.Success(file);
    }

    private Task<Result<FileResponse, IError>> InvokeAsync(string invoiceNumber)
        => InvokeAsync(_boundaryClient, invoiceNumber);

    private static FileResponse Value(Result<FileResponse, IError> result)
        => result.Match(v => v, _ => (FileResponse?)null)!;

    // ── Invoker half ──

    [Fact]
    public async Task Download_ReadsEveryByteAfterTheInvokerReturned()
    {
        var result = await InvokeAsync("INV-001").ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();

        using var file = Value(result);
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, CancellationToken.None).ConfigureAwait(true);

        buffer.ToArray().Should().Equal(Pdf,
            "the response must outlive the invoker — disposing it there truncates the download");
    }

    [Fact]
    public async Task Download_CarriesContentTypeAndFileNameBackAcrossTheHop()
    {
        var result = await InvokeAsync("INV-002").ConfigureAwait(true);

        using var file = Value(result);
        file.ContentType.Should().Be("application/pdf");
        file.FileName.Should().Be("INV-002.pdf");
    }

    [Fact]
    public async Task Download_CarriesTheETagBackAcrossTheHop()
    {
        var result = await InvokeAsync("INV-003").ConfigureAwait(true);

        using var file = Value(result);
        file.ETag.Should().Be(ETagValue);
    }

    [Fact]
    public async Task Download_InlineFile_KeepsBothTheFlagAndTheNameAcrossTheHop()
    {
        // Content-Disposition cannot carry this: an inline file emits no such header, and the file name
        // lived nowhere else. Before the out-of-band headers, both were lost at the boundary.
        var result = await InvokeAsync("inline-004").ConfigureAwait(true);

        using var file = Value(result);
        file.Inline.Should().BeTrue();
        file.FileName.Should().Be("inline-004.pdf");
    }

    [Fact]
    public async Task Download_MissingInvoice_SurfacesTheRemoteStatusAsAFailure()
    {
        var result = await InvokeAsync("missing").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        var error = result.Match<IError?>(_ => null, e => e);
        error!.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        error.Code.Should().Be("NOT_FOUND");
    }

    // ── End to end, through the host ──

    private async Task<HttpResponseMessage> GetAsync(string invoiceNumber, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/invoices/{invoiceNumber}/download");
        configure?.Invoke(request);
        return await _hostClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, CancellationToken.None)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task EndToEnd_NoRange_Returns200AndTheWholeFile()
    {
        using var response = await GetAsync("INV-100").ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(true);
        bytes.Should().Equal(Pdf);
    }

    [Fact]
    public async Task EndToEnd_InlineSurvivesToTheEndUser()
    {
        using var response = await GetAsync("inline-101").ConfigureAwait(true);

        // Inline means the browser displays it: no attachment disposition, same as a local endpoint.
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task EndToEnd_Attachment_KeepsTheFileNameOnContentDisposition()
    {
        using var response = await GetAsync("INV-102").ConfigureAwait(true);

        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be("INV-102.pdf");
    }

    [Fact]
    public async Task EndToEnd_Range_ReturnsExactlyThoseBytes()
    {
        // The test that a doubly-applied range fails. Asserting "206 and a plausible length" would pass
        // just as happily on a range of a range; only the bytes themselves distinguish the two.
        const int from = 1000;
        const int to = 1999;

        using var response = await GetAsync("INV-103",
            r => r.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to)).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(true);
        bytes.Should().Equal(Pdf[from..(to + 1)],
            "the host must forward the boundary's slice untouched — re-applying the request's Range to " +
            "content that is already an excerpt yields a range of a range, under a plausible status and " +
            "a plausible Content-Range, with nothing thrown");

        response.Content.Headers.ContentRange!.From.Should().Be(from);
        response.Content.Headers.ContentRange.To.Should().Be(to);
        response.Content.Headers.ContentRange.Length.Should().Be(Pdf.Length);
        response.Headers.AcceptRanges.Should().Contain("bytes");
    }

    [Fact]
    public async Task EndToEnd_SuffixRange_ReturnsTheLastBytes()
    {
        using var response = await GetAsync("INV-104",
            r => r.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, 500)).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(true);
        bytes.Should().Equal(Pdf[^500..]);
    }

    [Fact]
    public async Task EndToEnd_UnsatisfiableRange_Returns416AndNotAnEmpty206()
    {
        using var response = await GetAsync("INV-105",
            r => r.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(Pdf.Length + 5000, null))
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
    }

    [Fact]
    public async Task EndToEnd_IfRangeMatchingTheETag_StillServesTheRange()
    {
        using var response = await GetAsync("INV-106", r =>
        {
            r.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
            r.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(
                new System.Net.Http.Headers.EntityTagHeaderValue($"\"{ETagValue}\""));
        }).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(true);
        bytes.Should().Equal(Pdf[..100]);
    }

    [Fact]
    public async Task EndToEnd_IfRangeAgainstAStaleETag_FallsBackToTheWholeFile()
    {
        // RFC 9110: a non-matching If-Range means the client's cached copy is stale, so the whole
        // representation is served — 200, not 206.
        using var response = await GetAsync("INV-107", r =>
        {
            r.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
            r.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(
                new System.Net.Http.Headers.EntityTagHeaderValue("\"stale\""));
        }).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(true);
        bytes.Should().Equal(Pdf);
    }

    [Fact]
    public async Task EndToEnd_MissingInvoice_SurfacesAsThe404OfTheBoundary()
    {
        using var response = await GetAsync("missing").ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
