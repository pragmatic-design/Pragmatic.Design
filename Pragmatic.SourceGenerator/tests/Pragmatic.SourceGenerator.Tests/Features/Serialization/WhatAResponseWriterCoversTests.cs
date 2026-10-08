using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     Which response types get a generated writer, and what a writer does about the host's infrastructure
///     modifier: a type the writer could not reproduce is refused, with a reason, and keeps the serializer.
/// </summary>
public class WhatAResponseWriterCoversTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Pragmatic.Persistence.Entity;

        """;

    /// <summary>
    ///     The host strips <c>TenantId</c> everywhere and change tracking on an entity. The writer leaves out what
    ///     the modifier would, and says that it depends on the modifier.
    /// </summary>
    [Fact]
    public void AnInfrastructureMember_IsLeftOutAsTheHostsModifierLeavesItOut()
    {
        var compiled = CompiledResponseWriter.For(Usings + """
            namespace App;

            public sealed class Ticket : IChangeTracking
            {
                public Guid Id { get; init; }
                public Guid TenantId { get; init; }
                public string Title { get; init; } = "";
                public IReadOnlySet<string> ModifiedProperties { get; } = new HashSet<string> { "Title" };
                public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();
                public bool IsNew { get; set; }
                public void ResetModifiedProperties() { }
            }

            public static class Samples
            {
                public static object Ticket() => new Ticket { Id = Guid.Empty, TenantId = Guid.NewGuid(), Title = "t", IsNew = true };
            }
            """, CompiledResponseWriter.Named("App.Ticket"));

        compiled.Rejection.Should().BeNull();
        compiled.Plan!.NeedsInfrastructureExclusion.Should().BeTrue();

        var value = compiled.Sample("Ticket");
        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: true));
    }

    /// <summary>The control: a type with no reserved name writes the same either way, and says so.</summary>
    [Fact]
    public void ATypeWithNoReservedName_DoesNotDependOnTheModifier()
    {
        var compiled = CompiledResponseWriter.For(Usings + """
            namespace App;
            public sealed record Card(string Title, int Points);
            public static class Samples { public static object Card() => new Card("x", 3); }
            """, CompiledResponseWriter.Named("App.Card"));

        compiled.Plan!.NeedsInfrastructureExclusion.Should().BeFalse();

        var value = compiled.Sample("Card");
        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: false));
        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: true));
    }

    /// <summary>What a query answers with: a list of rows, written as the serializer writes the interface.</summary>
    [Fact]
    public void AListRoot_IsWrittenAsTheSerializerWritesIt()
    {
        var compiled = CompiledResponseWriter.For(Usings + """
            namespace App;
            public sealed record Row(string Name, int? Age);
            public static class Samples
            {
                public static object Rows() => (IReadOnlyList<Row>)new List<Row> { new("a", 1), new("b", null) };
            }
            """, compilation => compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1")!
            .Construct(compilation.GetTypeByMetadataName("App.Row")!));

        compiled.Rejection.Should().BeNull();

        var value = compiled.Sample("Rows");
        var declared = typeof(IReadOnlyList<>).MakeGenericType(value.GetType().GetGenericArguments()[0]);
        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, declared, excludesInfrastructure: false));
    }

    [Theory]
    [InlineData("public sealed class Dto { public Dto? Parent { get; init; } }", "reaches itself")]
    [InlineData("public sealed class Dto { public Child? Child { get; init; } } public sealed class Child { public Dto? Back { get; init; } }", "reaches itself")]
    [InlineData("public sealed class Dto { [JsonConverter(typeof(JsonStringEnumConverter))] public int X { get; init; } }", "[JsonConverter]")]
    [InlineData("[JsonConverter(typeof(JsonStringEnumConverter))] public sealed class Dto { public int X { get; init; } }", "[JsonConverter]")]
    [InlineData("public sealed class Dto { [JsonExtensionData] public Dictionary<string, object>? Extra { get; init; } }", "[JsonExtensionData]")]
    [InlineData("public sealed class Dto { public object? Anything { get; init; } }", "of type object")]
    [InlineData("public sealed class Dto { public Dictionary<Guid, string> ById { get; init; } = new(); }", "keyed by Guid")]
    [InlineData("public sealed class Dto { public Access Access { get; init; } } [Flags] public enum Access { Read = 1, Write = 2 }", "flags enum")]
    [InlineData("public sealed class Dto { public Alias Alias { get; init; } } public enum Alias { One = 1, Uno = 1 }", "two names to one value")]
    [InlineData("public sealed class Dto { public JsonElement Raw { get; init; } }", "JsonElement")]
    [InlineData("[JsonPolymorphic] [JsonDerivedType(typeof(Dto), \"dto\")] public class Dto { public int X { get; init; } }", "polymorphic")]
    public void AShapeTheWriterCannotReproduce_IsRefusedWithItsReason(string declaration, string reason)
    {
        var compiled = CompiledResponseWriter.For(Usings + "namespace App;\n" + declaration, CompiledResponseWriter.Named("App.Dto"));

        compiled.Plan.Should().BeNull();
        compiled.Rejection.Should().Contain(reason);
    }

    /// <summary>
    ///     A type the generated JSON context can describe and would name differently — it lowers only the first
    ///     letter — is refused: which of the two writes it depends on what the host registered.
    /// </summary>
    [Fact]
    public void ATypeTheContextWouldNameDifferently_IsRefused()
    {
        var compiled = CompiledResponseWriter.For(Usings + """
            namespace App;
            public sealed class Dto { public string URL { get; set; } = ""; }
            """, CompiledResponseWriter.Named("App.Dto"));

        compiled.Plan.Should().BeNull();
        compiled.Rejection.Should().Contain("generated JSON context");
    }

    /// <summary>The control: the same name on a type the context cannot describe has one writer, reflection.</summary>
    [Fact]
    public void ATypeOnlyReflectionCanDescribe_IsPlanned()
    {
        var compiled = CompiledResponseWriter.For(Usings + """
            namespace App;
            public sealed class Dto { public string URL { get; } = "u"; }
            """, CompiledResponseWriter.Named("App.Dto"));

        compiled.Rejection.Should().BeNull();
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("URL")]
    [InlineData("URLPath")]
    [InlineData("IOStream")]
    [InlineData("ABC")]
    [InlineData("A")]
    [InlineData("aB")]
    [InlineData("PersistenceId")]
    [InlineData("HTML5Content")]
    [InlineData("X1Y2")]
    [InlineData("Ünïcode")]
    [InlineData("MY NAME")]
    public void TheCamelCaseIsSystemTextJsons(string name)
        => JsonResponseMemberReader.CamelCase(name).Should().Be(JsonNamingPolicy.CamelCase.ConvertName(name));
}
