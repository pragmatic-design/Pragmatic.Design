using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Default values from property initializers flow into the manifest (defaultValue) and the
///     endpoint description is emitted. Runtime binding already honors initializers (absent
///     optional params never overwrite the constructed instance) — this is the documentation path.
/// </summary>
public class DefaultValueManifestTests : EndpointsGeneratorTestBase
{
    private const string EndpointWithDefaults = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace Test.Api;

        public class SearchResultDto { public string Name { get; set; } = ""; }

        [Endpoint(HttpVerb.Get, "/api/search")]
        [ApiSummary("Search items")]
        [ApiDescription("Full-text search over items.")]
        public partial class SearchEndpoint : Endpoint<SearchResultDto>
        {
            [FromQuery] public int PageSize { get; set; } = 20;
            [FromQuery] public string Currency { get; set; } = "eur";
            [FromQuery] public bool IncludeArchived { get; set; } = false;
            [FromQuery] public int Offset { get; set; } = -5;

            public override Task<Result<SearchResultDto>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<SearchResultDto>.Success(new SearchResultDto()));
        }
        """;

    [Fact]
    public void Manifest_QueryParamInitializers_EmitDefaultValue()
    {
        var result = RunGenerator(EndpointWithDefaults);


        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().NotBeNull();
        manifest.Should().Contain("\"defaultValue\": \"20\"");
        manifest.Should().Contain("\"defaultValue\": \"eur\"");
        manifest.Should().Contain("\"defaultValue\": \"false\"");
        manifest.Should().Contain("\"defaultValue\": \"-5\"");
    }

    [Fact]
    public void Manifest_EndpointDescription_IsEmitted()
    {
        var result = RunGenerator(EndpointWithDefaults);

        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().Contain("\"summary\": \"Search items\"");
        manifest.Should().Contain("\"description\": \"Full-text search over items.\"");
    }

    [Fact]
    public void Manifest_NonConstantInitializer_YieldsNoDefaultValue()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            public class ReportDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/reports")]
            public partial class ReportsEndpoint : Endpoint<ReportDto>
            {
                [FromQuery] public string RequestId { get; set; } = Guid.NewGuid().ToString();

                public override Task<Result<ReportDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<ReportDto>.Success(new ReportDto()));
            }
            """;

        var result = RunGenerator(source);


        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().NotContain("defaultValue", "non-constant initializers are not documentable");
    }
}
