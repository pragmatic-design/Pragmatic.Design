using System.Collections.Generic;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A remote boundary must be usable exactly like a local one, downloads included. The remote
///     implementation deserializes the JSON envelope, which a <c>FileResponse</c> cannot survive: it
///     carries a <see cref="System.IO.Stream" />, so <c>Deserialize&lt;FileResponse&gt;()</c> compiles
///     and then fails at runtime on a binary body. These tests pin the streaming branch that replaces it.
/// </summary>
public class RemoteFileActionTests
{
    internal const string BillingSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints.Responses;
        using Pragmatic.Result;

        namespace Billing
        {
            [Boundary]
            public sealed partial class BillingBoundary { }

            // Internal = false: no HTTP endpoint of its own, and still offered — the remote client
            // reaches it through the generic dispatcher. Without saying so it would be inferred
            // internal, which is the right default and the wrong answer here.
            [DomainAction(Internal = false)]
            [BelongsTo<BillingBoundary>]
            public sealed partial class DownloadInvoicePdfAction : DomainAction<FileResponse>
            {
                public Guid InvoiceId { get; init; }

                public override Task<Result<FileResponse, IError>> Execute(CancellationToken ct = default)
                    => throw new NotSupportedException();
            }

            // Exported for the same reason as the one above: this suite is about the remote transport,
            // not about visibility, and both actions have to be on the boundary's public surface.
            [DomainAction(Internal = false)]
            [BelongsTo<BillingBoundary>]
            public sealed partial class GetInvoiceNumberAction : DomainAction<string>
            {
                public Guid InvoiceId { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => throw new NotSupportedException();
            }
        }
        """;

    private static string RemoteBoundary()
    {
        var (sources, _) = TraitCompilationHarness.Generate(BillingSource);
        return Concat(sources, "Remote");
    }

    private static string Concat(IReadOnlyDictionary<string, string> sources, string hintFragment)
        => string.Join("\n", sources.Where(s => s.Key.Contains(hintFragment)).Select(s => s.Value));

    private static string MethodBody(string source, string methodName)
    {
        var start = source.IndexOf(methodName + "(global::", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the remote boundary must expose {methodName}");

        var next = source.IndexOf("/// <inheritdoc/>", start, System.StringComparison.Ordinal);
        return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
    }

    [Fact]
    public void FileAction_DoesNotDeserializeTheResponseAsJson()
    {
        var body = MethodBody(RemoteBoundary(), "DownloadInvoicePdf");

        // Deserialize<FileResponse>() on a binary body is the whole defect.
        body.Should().NotContain("Deserialize<global::Pragmatic.Endpoints.Responses.FileResponse>");
        body.Should().NotContain("PostAsJsonAsync");
    }

    [Fact]
    public void FileAction_ReadsTheResponseHeadersWithoutBufferingTheBody()
    {
        var body = MethodBody(RemoteBoundary(), "DownloadInvoicePdf");

        // Without ResponseHeadersRead HttpClient buffers the whole file into memory before returning.
        body.Should().Contain("global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead");
    }

    [Fact]
    public void FileAction_StreamsThroughRemoteFileResponse()
    {
        var body = MethodBody(RemoteBoundary(), "DownloadInvoicePdf");

        body.Should().Contain("global::Pragmatic.Endpoints.Responses.RemoteFileResponse.ReadAsync(response");
    }

    [Fact]
    public void FileAction_DoesNotDisposeTheResponseOnTheSuccessPath()
    {
        var body = MethodBody(RemoteBoundary(), "DownloadInvoicePdf");

        // The response must outlive this method: its content stream is what the caller reads. The only
        // Dispose allowed here is the failure one, which happens BEFORE the failure is returned.
        var successPart = body.Substring(body.IndexOf("RemoteFileResponse.ReadAsync", System.StringComparison.Ordinal));
        successPart.Should().NotContain("response.Dispose()");
    }

    [Fact]
    public void FileAction_DisposesTheResponseOnTheFailurePath()
    {
        var body = MethodBody(RemoteBoundary(), "DownloadInvoicePdf");

        // No stream is handed out on failure, so nobody else will ever release it.
        body.Should().Contain("response.Dispose();");
    }

    [Fact]
    public void FileAction_MapsTheStatusCodeToTheSameRemoteErrorAsEveryOtherAction()
    {
        var body = MethodBody(RemoteBoundary(), "DownloadInvoicePdf");

        // Same failure type and same envelope fields as the JSON path — no second convention.
        body.Should().Contain("new BillingRemoteError(errorCode, errorStatus, errorTitle)");
        body.Should().Contain("var errorStatus = (int)response.StatusCode;");
        body.Should().Contain("errorProp.TryGetProperty(\"status\", out var s)");
    }

    [Fact]
    public void NonFileAction_KeepsTheJsonEnvelopePath()
    {
        var body = MethodBody(RemoteBoundary(), "GetInvoiceNumber");

        body.Should().Contain("PostAsJsonAsync");
        // Typed, not generic-by-reflection: the untyped overload is RequiresUnreferencedCode and
        // under AOT returns nothing rather than the value. Through the remote-payload options, which
        // are the wire contract both ends share — the receiving host reads with the same naming.
        body.Should().Contain("valueProp.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<string>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options))");
        body.Should().NotContain("ResponseHeadersRead");
    }
}
