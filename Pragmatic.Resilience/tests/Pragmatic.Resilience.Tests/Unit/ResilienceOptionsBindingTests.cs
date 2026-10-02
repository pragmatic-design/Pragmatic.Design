using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Pipeline;

namespace Pragmatic.Resilience.Tests.Unit;

/// <summary>
/// Verifies the appsettings.json → <see cref="IOptions{ResilienceOptions}"/> binding path,
/// including the provider resolving pipelines built from bound configuration.
/// </summary>
public class ResilienceOptionsBindingTests
{
    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Bind_NamedPolicyWithRetryAndTimeout_PopulatesOptions()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:Policies:external-api:Retry:MaxRetries"] = "4",
            ["Resilience:Policies:external-api:Timeout:Timeout"] = "00:00:10",
        });

        var options = new ResilienceOptions();
        config.GetSection("Resilience").Bind(options);

        options.Policies.Should().ContainKey("external-api");
        var policy = options.Policies["external-api"];
        policy.Retry.Should().NotBeNull();
        policy.Retry!.MaxRetries.Should().Be(4);
        policy.Timeout.Should().NotBeNull();
        policy.Timeout!.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Bind_DefaultPolicy_PopulatesDefault()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:Default:Retry:MaxRetries"] = "2",
        });

        var options = new ResilienceOptions();
        config.GetSection("Resilience").Bind(options);

        options.Default.Should().NotBeNull();
        options.Default!.Retry!.MaxRetries.Should().Be(2);
    }

    [Fact]
    public void Bind_PolicyNames_AreCaseInsensitive()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:Policies:External-API:Retry:MaxRetries"] = "1",
        });

        var options = new ResilienceOptions();
        config.GetSection("Resilience").Bind(options);

        options.Policies.ContainsKey("external-api").Should().BeTrue();
    }

    [Fact]
    public void IOptions_BoundViaDI_FlowsIntoPipelineProvider()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:Policies:db:Retry:MaxRetries"] = "3",
            ["Resilience:Policies:db:Retry:BaseDelay"] = "00:00:00.001",
            ["Resilience:Policies:db:Retry:UseJitter"] = "false",
        });

        var services = new ServiceCollection();
        services.AddPragmaticResilience(options => config.GetSection("Resilience").Bind(options));

        using var provider = services.BuildServiceProvider();

        var bound = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;
        bound.Policies.Should().ContainKey("db");
        bound.Policies["db"].Retry!.MaxRetries.Should().Be(3);

        var pipelineProvider = provider.GetRequiredService<IResiliencePipelineProvider>();
        pipelineProvider.GetPipeline("db").Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public async Task IOptions_BoundRetryPolicy_RetriesAtConfiguredCount()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:Policies:db:Retry:MaxRetries"] = "2",
            ["Resilience:Policies:db:Retry:BaseDelay"] = "00:00:00.001",
            ["Resilience:Policies:db:Retry:UseJitter"] = "false",
        });

        var services = new ServiceCollection();
        services.AddPragmaticResilience(options => config.GetSection("Resilience").Bind(options));
        using var serviceProvider = services.BuildServiceProvider();

        var pipeline = serviceProvider.GetRequiredService<IResiliencePipelineProvider>().GetPipeline("db");
        var calls = 0;

        var result = await pipeline.ExecuteAsync<int>(
            (_, _) =>
            {
                calls++;
                if (calls < 3)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(99);
            },
            new ResilienceContext { OperationName = "db" }, CancellationToken.None);

        result.Should().Be(99);
        calls.Should().Be(3); // initial + 2 retries
    }

    [Fact]
    public void EmptySection_LeavesOptionsAtDefaults()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>());

        var options = new ResilienceOptions();
        config.GetSection("Resilience").Bind(options);

        options.Default.Should().BeNull();
        options.Policies.Should().BeEmpty();
    }
}
