using System.Net;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An enum parameter binds the values its enum defines, by name or by number, and refuses the others with a 400.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The binder was <c>Enum.TryParse</c>, which accepts any integer: <c>?status=99</c> reached the endpoint as
///         a value no member names, and a sort direction of <c>5</c> sorted descending because the generated
///         <c>Apply</c> reads anything but ascending as its <c>else</c>. A name the enum did not know was refused; a
///         number it did not know was not.
///     </para>
///     <para>
///         The endpoint is the generated one, compiled and mapped into a test server, so the 400 is the one a caller
///         gets: one parameter is required, the other optional, which are the binder's two generated paths.
///     </para>
/// </remarks>
[Collection(Unit.TheProcessWideOpenApiRegistryCollection.Name)]   // loading the endpoint registers its manifest
public class AnEnumParameterRefusesAnUndefinedNumberTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp.Items;

        public enum ItemStatus { Draft = 0, Active = 1, Retired = 2 }

        [Flags]
        public enum Access { None = 0, Read = 1, Write = 2, Share = 4 }

        [Endpoint(HttpVerb.Get, "/api/items")]
        public partial class ListItemsEndpoint : Endpoint<string>
        {
            [Microsoft.AspNetCore.Mvc.FromQuery]
            public required ItemStatus Status { get; set; }

            [Microsoft.AspNetCore.Mvc.FromQuery]
            public Access? Access { get; set; }

            public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<string>>($"{Status}|{Access}");
        }
        """;

    private static readonly Lazy<MethodInfo> MapEndpoint = new(Compile);

    [Fact]
    public async Task ANumberTheEnumDoesNotDefine_Is400()
    {
        using var host = await Host();

        var response = await host.GetTestClient().GetAsync("/api/items?status=99");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "99 is no ItemStatus, and an unknown number is as malformed as an unknown name");
    }

    [Fact]
    public async Task AFlagTheEnumDoesNotDefine_Is400()
    {
        using var host = await Host();

        var response = await host.GetTestClient().GetAsync("/api/items?status=Active&access=8");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "8 is no Access bit, alone or combined");
    }

    [Theory]
    [InlineData("status=1", "\"Active|\"")]
    [InlineData("status=active", "\"Active|\"")]
    [InlineData("status=Retired&access=3", "\"Retired|Read, Write\"")]
    [InlineData("status=0&access=Read,Share", "\"Draft|Read, Share\"")]
    public async Task AValueTheEnumDefines_Binds(string query, string expected)
    {
        using var host = await Host();

        var response = await host.GetTestClient().GetAsync($"/api/items?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    private static async Task<WebApplication> Host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver());

        var app = builder.Build();
        MapEndpoint.Value.Invoke(null, [app]);
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static MethodInfo Compile()
    {
        var result = RunGenerator(Source,
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Endpoints.Binding.RequestJson)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Buffers.ReadOnlySequence<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Uri)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Enumerable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Threading.RateLimiting.RateLimitPartition)));
        GetCompilationErrors(result).Should().BeEmpty("the generated endpoint must compile");

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = Assembly.Load(image.ToArray());
        return assembly.GetTypes()
            .Select(t => t.GetMethod("MapEndpoint", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(IEndpointRouteBuilder)]))
            .Single(m => m is not null)!;
    }
}
