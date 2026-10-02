using System.Reflection;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     End-to-end integration: runs the real source generator over a <c>[Cacheable]</c> type,
///     emits and loads the generated assembly, then drives the produced <see cref="ICacheable"/>
///     implementation through a live <see cref="HybridCacheStack"/>.
/// </summary>
public sealed class CacheableRuntimeIntegrationTests : CachingGeneratorTestBase, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly HybridCacheStack _cache;

    public CacheableRuntimeIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        _cache = new HybridCacheStack(_provider.GetRequiredService<HybridCache>());
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task GeneratedCacheable_DrivesHybridCacheStack_RoundTrips()
    {
        const string source = """
                              using Pragmatic.Caching.Attributes;

                              namespace IntegrationNs;

                              [Cacheable(Duration = "30m", Tags = new[] { "users", "tenant:{TenantId}" })]
                              public partial class GetUser
                              {
                                  public int TenantId { get; init; }
                                  public int UserId { get; init; }
                              }
                              """;

        var cacheable = (ICacheable)CompileAndInstantiate(source, "IntegrationNs.GetUser",
            instance =>
            {
                instance.GetType().GetProperty("TenantId")!.SetValue(instance, 7);
                instance.GetType().GetProperty("UserId")!.SetValue(instance, 99);
            });

        var key = cacheable.GetCacheKey();
        var options = cacheable.GetCacheOptions();

        key.Should().NotBeNullOrWhiteSpace();
        key.Should().Contain("7").And.Contain("99");
        options.Duration.Should().Be(TimeSpan.FromMinutes(30));
        options.Tags.Should().Contain("users");
        // Placeholder {TenantId} expands to the property value at runtime.
        options.Tags.Should().Contain("tenant:7");

        // Drive the value through a real HybridCacheStack using the generated key + options.
        var factoryCalls = 0;
        var first = await _cache.GetOrSetAsync(
            key,
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult("user-payload");
            },
            options);

        var second = await _cache.GetOrSetAsync(
            key,
            _ => ValueTask.FromResult("should-not-be-used"),
            options);

        first.Should().Be("user-payload");
        second.Should().Be("user-payload", "the second call must be served from cache");
        factoryCalls.Should().Be(1, "the generated key must produce a stable cache hit");
    }

    [Fact]
    public void GeneratedCacheable_SameInputs_ProduceSameKey()
    {
        const string source = """
                              using Pragmatic.Caching.Attributes;

                              namespace IntegrationNs;

                              [Cacheable(Duration = "5m")]
                              public partial class GetProduct
                              {
                                  public int ProductId { get; init; }
                              }
                              """;

        var asm = CompileToAssembly(source);
        var type = asm.GetType("IntegrationNs.GetProduct")!;

        var a = (ICacheable)Activate(type, i => type.GetProperty("ProductId")!.SetValue(i, 5));
        var b = (ICacheable)Activate(type, i => type.GetProperty("ProductId")!.SetValue(i, 5));
        var c = (ICacheable)Activate(type, i => type.GetProperty("ProductId")!.SetValue(i, 6));

        a.GetCacheKey().Should().Be(b.GetCacheKey(), "identical inputs must yield identical keys");
        a.GetCacheKey().Should().NotBe(c.GetCacheKey(), "different inputs must yield different keys");
    }

    [Fact]
    public void GeneratedCacheable_DefaultCategory_IsNull()
    {
        const string source = """
                              using Pragmatic.Caching.Attributes;

                              namespace IntegrationNs;

                              [Cacheable(Duration = "5m")]
                              public partial class GetThing
                              {
                                  public int ThingId { get; init; }
                              }
                              """;

        var cacheable = (ICacheable)CompileAndInstantiate(source, "IntegrationNs.GetThing",
            i => i.GetType().GetProperty("ThingId")!.SetValue(i, 1));

        cacheable.CacheCategory.Should().BeNull("no Category was specified on [Cacheable]");
    }

    // --- helpers (ConfigureAwait not relevant: synchronous reflection/compile work) ---

    private static object CompileAndInstantiate(string source, string typeName, Action<object> configure)
    {
        var asm = CompileToAssembly(source);
        var type = asm.GetType(typeName)
                   ?? throw new InvalidOperationException($"Generated type '{typeName}' not found.");
        return Activate(type, configure);
    }

    private static object Activate(Type type, Action<object> configure)
    {
        var instance = Activator.CreateInstance(type)
                       ?? throw new InvalidOperationException($"Could not instantiate '{type.FullName}'.");
        configure(instance);
        return instance;
    }

    private static Assembly CompileToAssembly(string source)
    {
        var result = RunGenerator(source);

        GetCompilationErrors(result).Should().BeEmpty("generated code must compile cleanly");

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "emit must succeed: " + string.Join("; ", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }
}
