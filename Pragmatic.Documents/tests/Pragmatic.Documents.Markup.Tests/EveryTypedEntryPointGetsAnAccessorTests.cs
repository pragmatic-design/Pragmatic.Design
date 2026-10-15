using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Data.Providers;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
/// Each typed way of giving a template data marks its type parameter <c>[TemplateData]</c>, and the
/// generator writes accessors for whatever type a call binds to it, extension members and constructions
/// included. The calls below are compiled, not run: what is under test is what the generator saw.
/// </summary>
public class EveryTypedEntryPointGetsAnAccessorTests
{
    [Fact]
    public async Task ACatalogSource_IsReadWithoutReflection()
    {
        var context = new DataSourceCatalog().Add("item", new CatalogItem { Code = "A-1" }).ToDataContext();

        var value = await context.ResolveAsync("item.code");

        value.Should().Be("A-1");
        ReflectionPropertyAccessor.HasRead(typeof(CatalogItem)).Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(AsyncItem))]
    [InlineData(typeof(JsonItem))]
    [InlineData(typeof(JsonRow))]
    [InlineData(typeof(StaticItem))]
    [InlineData(typeof(AsyncFactoryItem))]
    public void ATypeBoundToAMarkedParameter_HasAnAccessor(Type type)
        => TemplateAccessors.TryGet(type, out _).Should().BeTrue();

    [Fact]
    public void ATypeNoTemplateIsGiven_HasNone()
        => TemplateAccessors.TryGet(typeof(NeverGiven), out _).Should().BeFalse();

    // Never called: these calls exist for the generator to find.
    private static void Calls(DataSourceCatalog catalog)
    {
        catalog.AddAsync("async", _ => ValueTask.FromResult(new AsyncItem()));
        catalog.AddJsonFile("json", "item.json", (JsonTypeInfo<JsonItem>)null!);
        catalog.AddJsonFileList("rows", "rows.json", (JsonTypeInfo<List<JsonRow>>)null!);
        _ = new StaticDataSource<StaticItem>("static", new StaticItem());
        _ = new AsyncDataSource<AsyncFactoryItem>("factory", _ => ValueTask.FromResult(new AsyncFactoryItem()));
        _ = new NeverGiven();
    }

    public sealed class CatalogItem { public string Code { get; init; } = ""; }

    public sealed class AsyncItem { public string Code { get; init; } = ""; }

    public sealed class JsonItem { public string Code { get; init; } = ""; }

    public sealed class JsonRow { public string Code { get; init; } = ""; }

    public sealed class StaticItem { public string Code { get; init; } = ""; }

    public sealed class AsyncFactoryItem { public string Code { get; init; } = ""; }

    public sealed class NeverGiven { public string Code { get; init; } = ""; }
}
