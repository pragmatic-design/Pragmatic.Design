using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Caching.Models;
using Pragmatic.SourceGenerator.Features.Caching.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Caching;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// </summary>
public class CacheableTemplateTests
{
    [Fact]
    public void RenderOutput_SimpleModel_HintNameUsesVirtualFolder()
    {
        var model = BuildModel("MyApp.Catalog", "GetPropertyQuery");

        var artifact = new CacheableTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Catalog.GetPropertyQuery.Cache.g.cs");
    }

    [Fact]
    public void RenderOutput_NoNamespace_HintNameHasNoFolder()
    {
        var model = BuildModel(string.Empty, "SimpleQuery");

        var artifact = new CacheableTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("SimpleQuery.Cache.g.cs");
    }

    [Fact]
    public void RenderOutput_WithKeyProperties_GeneratesGetCacheKeyWithInterpolation()
    {
        var model = BuildModel("MyApp", "UserQuery",
            keys: [new CacheKeyPropertyModel { Name = "UserId", KeyName = "UserId", Type = "int", Order = 0,
                Parts = [new CacheKeyPartModel("UserId", "", false)] }]);

        var source = new CacheableTemplate(model).RenderOutput().Text;

        source.Should().Contain("GetCacheKey");
        // Value is URI-escaped so a value containing ':' or '=' can't collide with another key segment.
        source.Should().Contain("UserQuery:UserId={System.Uri.EscapeDataString(");
        source.Should().Contain("UserId");
    }

    [Fact]
    public void RenderOutput_NoKeyProperties_GeneratesStaticTypeNameKey()
    {
        var model = BuildModel("MyApp", "AllUsersQuery", keys: []);

        var source = new CacheableTemplate(model).RenderOutput().Text;

        // The cache key uses the fully-qualified type name (Namespace.TypeName) so two same-named
        // queries in different namespaces can't collide on the same key.
        source.Should().Contain("\"MyApp.AllUsersQuery\"");
        // Static helper generates Prefix property, not Create method
        source.Should().Contain("Prefix");
    }

    [Fact]
    public void RenderOutput_WithDuration_GeneratesCorrectTimeSpan()
    {
        var model = BuildModel("MyApp", "CachedQuery", duration: "10m");

        var source = new CacheableTemplate(model).RenderOutput().Text;

        // 10 minutes = 600 seconds
        source.Should().Contain("600");
        source.Should().Contain("Duration");
    }

    [Fact]
    public void RenderOutput_SlidingExpiration_GeneratesSlidingDuration()
    {
        var model = BuildModel("MyApp", "CachedQuery", duration: "5m", sliding: true);

        var source = new CacheableTemplate(model).RenderOutput().Text;

        source.Should().Contain("SlidingDuration");
    }

    [Fact]
    public void RenderOutput_WithTags_GeneratesTagsInOptions()
    {
        var model = BuildModel("MyApp", "CachedQuery", tags: ["users", "catalog"]);

        var source = new CacheableTemplate(model).RenderOutput().Text;

        source.Should().Contain("Tags");
        source.Should().Contain("\"users\"");
        source.Should().Contain("\"catalog\"");
    }

    [Fact]
    public void RenderOutput_MultipleKeyProperties_CacheKeysHelperHasCreateMethod()
    {
        var model = BuildModel("MyApp", "OrderQuery",
            keys:
            [
                new CacheKeyPropertyModel { Name = "UserId", KeyName = "UserId", Type = "int", Order = 0,
                Parts = [new CacheKeyPartModel("UserId", "", false)] },
                new CacheKeyPropertyModel { Name = "OrderId", KeyName = "OrderId", Type = "int", Order = 1,
                Parts = [new CacheKeyPartModel("OrderId", "", false)] }
            ]);

        var source = new CacheableTemplate(model).RenderOutput().Text;

        source.Should().Contain("Create");
        // Static helper key parts are URI-escaped too (camelCased parameter names).
        source.Should().Contain("OrderQuery:UserId={System.Uri.EscapeDataString(");
        source.Should().Contain(":OrderId={System.Uri.EscapeDataString(");
        source.Should().Contain("userId");
        source.Should().Contain("orderId");
    }

    [Fact]
    public void RenderOutput_RecordType_GeneratesRecord()
    {
        var model = BuildModel("MyApp", "UserQuery", typeKind: "record");

        var source = new CacheableTemplate(model).RenderOutput().Text;

        source.Should().Contain("partial record UserQuery");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static CacheableModel BuildModel(
        string ns,
        string typeName,
        string duration = "5m",
        bool sliding = false,
        ImmutableArray<CacheKeyPropertyModel>? keys = null,
        ImmutableArray<string>? tags = null,
        string typeKind = "class") => new()
        {
            Namespace = ns,
            TypeName = typeName,
            Accessibility = "public",
            TypeKind = typeKind,
            Duration = duration,
            Sliding = sliding,
            Tags = tags ?? ImmutableArray<string>.Empty,
            KeyProperties = keys ?? ImmutableArray<CacheKeyPropertyModel>.Empty,
            IsPartial = true,
            TotalPropertyCount = keys?.Length ?? 0,
            AllPropertyNames = keys?.Select(k => k.Name).ToImmutableArray() ?? ImmutableArray<string>.Empty
        };
}
