using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     [FromCookie] binding: cookies are read from HttpContext.Request.Cookies in the handler
///     body (minimal APIs have no native cookie binding), required missing → 400, typed values
///     parsed with TryParse, and the property is excluded from the body DTO.
/// </summary>
public class FromCookieBindingTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void FromCookie_RequiredString_BindsWith400OnMissing()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class SessionDto { public string Value { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/session")]
            public partial class GetSessionEndpoint : Endpoint<SessionDto>
            {
                [FromCookie("session-id")]
                public string SessionId { get; set; } = "";

                public override Task<Result<SessionDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<SessionDto>.Success(new SessionDto()));
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "GetSessionEndpoint");
        generated.Should().Contain("httpContext.Request.Cookies.TryGetValue(\"session-id\", out var sessionIdCookie);");
        generated.Should().Contain("Missing required cookie: session-id");
        generated.Should().Contain("statusCode: 400");
    }

    [Fact]
    public void FromCookie_OptionalGuid_ParsesWithTryParse()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class SessionDto { public string Value { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/session")]
            public partial class GetSessionEndpoint : Endpoint<SessionDto>
            {
                [FromCookie("device-id", IsRequired = false)]
                public Guid DeviceId { get; set; }

                public override Task<Result<SessionDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<SessionDto>.Success(new SessionDto()));
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "GetSessionEndpoint");
        generated.Should().Contain("System.Guid.TryParse(deviceIdCookie");
        generated.Should().NotContain("Missing required cookie");
    }

    [Fact]
    public void FromCookie_Property_IsExcludedFromBodyDto()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/notes")]
            public partial class CreateNoteEndpoint : Endpoint<NoteDto>
            {
                [FromCookie("session-id")]
                public string SessionId { get; set; } = "";

                public required string Text { get; set; }

                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        var bodyDto = GetGeneratedSource(result, "CreateNoteEndpoint.RequestBody");
        bodyDto.Should().NotBeNull();
        bodyDto.Should().Contain("Text");
        bodyDto.Should().NotContain("SessionId", "cookie-bound properties must not appear in the body DTO");
    }

    [Fact]
    public void FromCookie_Manifest_DocumentsCookieParameter()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class SessionDto { public string Value { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/session")]
            public partial class GetSessionEndpoint : Endpoint<SessionDto>
            {
                [FromCookie("session-id")]
                public string SessionId { get; set; } = "";

                public override Task<Result<SessionDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<SessionDto>.Success(new SessionDto()));
            }
            """;

        var result = RunGenerator(source);

        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().Contain("\"name\": \"session-id\"");
        manifest.Should().Contain("\"in\": \"cookie\"");
    }
}
