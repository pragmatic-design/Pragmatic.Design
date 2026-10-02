using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The serving half of a remote download. <c>/_pragmatic/invoke</c> answers with a JSON envelope
///     whose <c>value</c> is the serialized success — which for a <c>FileResponse</c> would emit the
///     record's shape and leave the bytes unread. File actions must stream instead.
/// </summary>
public class PragmaticInvokeEndpointFileActionTests
{
    private const string FileResponseFqn = "global::Pragmatic.Endpoints.Responses.FileResponse";

    private static string Render()
    {
        var actions = ImmutableArray.Create(
            new DiscoveredActionInfo
            {
                ActionType = "Billing.DownloadInvoicePdfAction",
                IsVoid = false,
                ReturnType = FileResponseFqn,
                InvokerType = "global::Billing.DownloadInvoicePdfActionInvoker",
                SourceAssembly = "Billing"
            },
            new DiscoveredActionInfo
            {
                ActionType = "Billing.GetInvoiceNumberAction",
                IsVoid = false,
                ReturnType = "string",
                InvokerType = "global::Billing.GetInvoiceNumberActionInvoker",
                SourceAssembly = "Billing"
            });

        return new PragmaticInvokeEndpointTemplate(actions, "TestHost").RenderOutput().Text;
    }

    [Fact]
    public void FileAction_StreamsTheBytesInsteadOfSerializingTheEnvelope()
    {
        var source = Render();

        source.Should().Contain(
            $"PragmaticInvokeDispatcher.InvokeFileActionAsync<global::Billing.DownloadInvoicePdfAction, {FileResponseFqn}>");
    }

    [Fact]
    public void FileAction_WritesTheFileWithTheSameHelperAsTheLocalEndpointPath()
    {
        var source = Render();

        // Same helper as the in-process endpoint, so Content-Type / Content-Disposition / ETag do not
        // depend on whether the boundary happens to be remote.
        source.Should().Contain("global::Pragmatic.Endpoints.Extensions.FileResponseExtensions.ToResult(__file, null");
    }

    [Fact]
    public void FileAction_AsksForTheOutOfBandMetadataHeaders()
    {
        var source = Render();

        // The caller is another Pragmatic host rebuilding the FileResponse. Content-Disposition alone
        // cannot tell it the file is inline — for an inline file that header is absent altogether, and
        // the file name goes with it.
        source.Should().Contain("includeRpcMetadata: true");
    }

    [Fact]
    public void NonFileAction_IsUnchangedByTheFilePath()
    {
        var source = Render();

        // The metadata headers are a file concern: the JSON envelope dispatch must be untouched, and
        // the flag must appear exactly once — on the one file action.
        source.Should().Contain(
            "return PragmaticInvokeDispatcher.InvokeActionAsync<global::Billing.GetInvoiceNumberAction, string>(");
        System.Text.RegularExpressions.Regex.Matches(source, "includeRpcMetadata").Count.Should().Be(1);
    }

    [Fact]
    public void NonFileAction_KeepsTheJsonEnvelopeDispatch()
    {
        var source = Render();

        source.Should().Contain(
            "PragmaticInvokeDispatcher.InvokeActionAsync<global::Billing.GetInvoiceNumberAction, string>");
    }

    [Fact]
    public void FileAction_DoesNotGoThroughTheJsonEnvelopeDispatch()
    {
        var source = Render();

        source.Should().NotContain(
            $"PragmaticInvokeDispatcher.InvokeActionAsync<global::Billing.DownloadInvoicePdfAction, {FileResponseFqn}>");
    }
}
