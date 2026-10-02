using System.Collections.Generic;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class PragmaticJsonOptionsTests
{
    private sealed class Poco
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    [Fact]
    public void Build_Default_UsesCamelCaseAndReflectionFallback()
    {
        var options = new PragmaticJsonOptions().Build();

        var json = JsonSerializer.Serialize(new Poco { Name = "x", Count = 2 }, options);

        // camelCase policy applies, and the reflection fallback lets an arbitrary POCO serialize.
        json.Should().Contain("\"name\"").And.Contain("\"count\"");
    }

    [Fact]
    public void Build_IsCachedAndReadOnly()
    {
        var seam = new PragmaticJsonOptions();

        var first = seam.Build();
        var second = seam.Build();

        first.Should().BeSameAs(second);
        first.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void AddContext_AfterBuild_Throws()
    {
        var seam = new PragmaticJsonOptions();
        seam.Build();

        var act = () => seam.AddContext(PragmaticCommonJsonContext.Default);

        act.Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void AddContextOnce_IsIdempotent()
    {
        var seam = new PragmaticJsonOptions();

        seam.AddContextOnce(PragmaticCommonJsonContext.Default);
        seam.AddContextOnce(PragmaticCommonJsonContext.Default);

        // The common context is seeded once by the ctor; adding the same instance again is a no-op.
        seam.Contexts.Should().ContainSingle(c => ReferenceEquals(c, PragmaticCommonJsonContext.Default));
    }

    [Fact]
    public void DisableReflectionFallback_CoveredTypeSerializes_UncoveredTypeThrows()
    {
        // The common context covers Dictionary<string,string>; nothing covers Poco.
        var options = new PragmaticJsonOptions()
            .DisableReflectionFallback()
            .Build();

        // Covered by the seeded PragmaticCommonJsonContext → serializes with no reflection.
        var dict = new Dictionary<string, string> { ["en"] = "Widget" };
        JsonSerializer.Serialize(dict, options).Should().Contain("Widget");

        // Not covered and no reflection fallback → STJ refuses instead of falling back to reflection.
        var act = () => JsonSerializer.Serialize(new Poco { Name = "x" }, options);
        act.Should().Throw<System.NotSupportedException>();
    }
}
