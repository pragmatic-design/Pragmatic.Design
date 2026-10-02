using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Steps;
using Pragmatic.Http;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     RequestLimitsStep: per-endpoint MaxBodySizeMetadata (from [MaxBodySize]) and the global
///     configuration default must reject oversized declared bodies with 413 before the handler runs.
/// </summary>
public class RequestLimitsStepTests
{
    [Fact]
    public async Task EndpointMetadata_OversizedContentLength_Returns413()
    {
        var context = CreateContext(contentLength: 2048, maxBytesMetadata: 1024);
        var nextCalled = false;

        await InvokePipeline(context, configuration: null, () => nextCalled = true);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        nextCalled.Should().BeFalse("oversized requests must never reach the handler");
    }

    [Fact]
    public async Task EndpointMetadata_SmallBody_PassesThrough()
    {
        var context = CreateContext(contentLength: 100, maxBytesMetadata: 1024);
        var nextCalled = false;

        await InvokePipeline(context, configuration: null, () => nextCalled = true);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GlobalConfigDefault_AppliesWhenNoMetadata()
    {
        var context = CreateContext(contentLength: 2048, maxBytesMetadata: null);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pragmatic:RequestLimits:MaxBodySizeBytes"] = "512"
            })
            .Build();
        var nextCalled = false;

        await InvokePipeline(context, configuration, () => nextCalled = true);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task NoLimitConfigured_PassesThrough()
    {
        var context = CreateContext(contentLength: 1_000_000, maxBytesMetadata: null);
        var nextCalled = false;

        await InvokePipeline(context, configuration: null, () => nextCalled = true);

        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext CreateContext(long contentLength, long? maxBytesMetadata)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = contentLength;

        if (maxBytesMetadata is { } maxBytes)
        {
            var endpoint = new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new MaxBodySizeMetadata(maxBytes)),
                "test-endpoint");
            context.SetEndpoint(endpoint);
        }

        return context;
    }

    private static async Task InvokePipeline(HttpContext context, IConfiguration? configuration, Action onNext)
    {
        // Stateless step: the global default is read from IConfiguration in ApplicationServices.
        var services = new ServiceCollection()
            .AddSingleton(configuration ?? new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var builder = new ApplicationBuilder(services);
        new RequestLimitsStep().ConfigurePipeline(builder);
        builder.Run(_ =>
        {
            onNext();
            return Task.CompletedTask;
        });

        await builder.Build()(context).ConfigureAwait(false);
    }
}
