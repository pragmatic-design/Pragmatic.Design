using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A request body past a limit the endpoint itself declared is answered, not thrown.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>ReadFormAsync</c> throws <c>InvalidDataException</c> when the multipart body exceeds
///         <c>RequestFormLimits.MultipartBodyLengthLimit</c>, and nobody caught it — so an upload one
///         byte over the ceiling answered <b>500</b>, with a stack trace, for a request the endpoint had
///         refused on purpose. Measured on a consumer application whose attachment ceiling is 5 MB.
///     </para>
///     <para>
///         ⚠️ And it made a generated check unreachable: <c>[HasAttachments]</c> emits both the form
///         limit and a <c>file.Length &gt; max</c> branch answering 413, and the branch cannot run,
///         because the form read that would have to precede it throws first. A generated mechanism that
///         cannot be reached is the same defect as one nobody wired.
///     </para>
///     <para>
///         413 rather than 400: the request is well formed and the server declined its size, which is
///         what RFC 9110 has a status for — the same reasoning as the 415 beside it in
///         <c>BindingFailure</c>, which exists because that case was a 500 too.
///     </para>
/// </remarks>
public class AFormBodyOverTheLimitTests : EndpointsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Uploads;
        """;

    private const string FormAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/uploads")]
        public partial class UploadFileAction : DomainAction<Guid>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    private const string NoFormAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/plain")]
        public partial class PlainAction : DomainAction<Guid>
        {
            public required Guid Id { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Id));
        }
        """;

    [Fact]
    public void AFormRead_AnswersWhenTheBodyIsOverTheDeclaredLimit()
    {
        var result = RunGenerator(FormAction);

        var handler = GetGeneratedSource(result, "UploadFileAction.Endpoint")!;

        handler.Should().Contain("catch (global::System.IO.InvalidDataException",
            "the read throws for a body past the declared form limit, and an uncaught throw is a 500");
        handler.Should().Contain("WritePayloadTooLargeAsync",
            "the caller is told the size was refused, with the status that says so");
    }

    /// <summary>
    ///     The control: an endpoint that reads no form gains nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the guard is emitted" would be satisfied by wrapping every handler in a catch
    ///     for an exception its body can never raise.
    /// </remarks>
    [Fact]
    public void AnEndpointWithNoForm_GainsNoSuchGuard()
    {
        var result = RunGenerator(NoFormAction);

        GetGeneratedSource(result, "PlainAction.Endpoint")!
            .Should().NotContain("WritePayloadTooLargeAsync");
    }
}
