using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     The producer of <c>MarkSet</c>: the JSON converter generated for a <c>[Patch&lt;T&gt;]</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>ApplyPatch</c> had two branches and only one of them was ever reached: nothing on the
///         HTTP path called <c>MarkSet</c>, so a patch read from a body always took the "is not null"
///         fallback, where <c>{"x": null}</c> and a body without <c>x</c> are the same thing.
///     </para>
///     <para>
///         The generated code is executed here, not read: the converter is emitted with the patch
///         into its own assembly and driven through <c>System.Text.Json</c>, because a converter that
///         is declared and never invoked is exactly the defect this exists to close.
///     </para>
/// </remarks>
public class PatchJsonConverterTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Patch;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order
        {
            public string Reference { get; set; } = "";
            public string? Notes { get; set; }
            public int Quantity { get; set; }
        }

        [Patch<Order>]
        public partial class UpdateOrder
        {
            public string? Reference { get; init; }
            public string? Notes { get; init; }
            public int Quantity { get; init; }
            public List<string> Tags { get; init; } = new();
        }
        """;

    [Fact]
    public void TheConverter_IsDeclaredOnThePatch_AndMarksWhatItReads()
    {
        var generated = GeneratorTestHelper.GetGeneratedSource(RunGenerator(Source), "UpdateOrder.PatchJsonConverter");

        generated.Should().NotBeNull();
        generated!.Should().Contain("[global::System.Text.Json.Serialization.JsonConverter(typeof(UpdateOrder.PatchJsonConverter))]",
            "declared on the type, so whatever options read the body honour it");
        generated.Should().Contain("__result.MarkSet(nameof(Reference))");
        generated.Should().Contain("__result.MarkSet(nameof(Tags))");
    }

    /// <summary>
    ///     ⚠️ The distinction a PATCH exists to make: a null that was sent is marked; a property that
    ///     was not sent is not.
    /// </summary>
    [Fact]
    public void AnExplicitNull_IsMarked_AndAnAbsentPropertyIsNot()
    {
        var patchType = EmitAndLoad(RunGenerator(Source)).GetType("TestApp.UpdateOrder")!;

        var patch = JsonSerializer.Deserialize("""{"reference":null,"quantity":3}""", patchType, CamelCase)!;

        SetPropertiesOf(patch).Should().BeEquivalentTo(new[] { "Reference", "Quantity" },
            "the body named these two, one of them with a null");
        patchType.GetProperty("Reference")!.GetValue(patch).Should().BeNull();
        patchType.GetProperty("Quantity")!.GetValue(patch).Should().Be(3);
        patchType.GetProperty("Notes")!.GetValue(patch).Should().BeNull("not sent, so left at its default");
        patchType.GetProperty("Tags")!.GetValue(patch).Should().NotBeNull(
            "not sent, so left at the initializer the author wrote — not at default(T)");
    }

    /// <summary>An empty body marks nothing, so <c>ApplyPatch</c> takes its fallback as before.</summary>
    [Fact]
    public void AnEmptyBody_MarksNothing()
    {
        var patchType = EmitAndLoad(RunGenerator(Source)).GetType("TestApp.UpdateOrder")!;

        var patch = JsonSerializer.Deserialize("{}", patchType, CamelCase)!;

        SetPropertiesOf(patch).Should().BeEmpty();
    }

    /// <summary>
    ///     Writing keeps the reading: what was marked is written, null included, and what was not
    ///     marked is not — so a patch that crosses a second wire still says the same thing.
    /// </summary>
    [Fact]
    public void Writing_KeepsTheNullThatWasSent_AndLeavesOutWhatWasNot()
    {
        var patchType = EmitAndLoad(RunGenerator(Source)).GetType("TestApp.UpdateOrder")!;
        var patch = JsonSerializer.Deserialize("""{"reference":null,"quantity":3}""", patchType, CamelCase)!;

        var json = JsonSerializer.Serialize(patch, patchType, CamelCase);

        json.Should().Contain("\"reference\":null");
        json.Should().Contain("\"quantity\":3");
        json.Should().NotContain("notes");
        json.Should().NotContain("tags");
    }

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static IReadOnlySet<string> SetPropertiesOf(object patch)
        => (IReadOnlySet<string>)patch.GetType().GetProperty("SetProperties")!.GetValue(patch)!;

    /// <summary>
    ///     Compiles the source with the two files under test — the patch and its converter — and
    ///     nothing else the generator emitted: the repositories and registrations need a reference
    ///     closure this harness does not carry, and are not what is being executed here.
    /// </summary>
    private static Assembly EmitAndLoad(SourceGenRunResult result)
    {
        var trees = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("UpdateOrder.Patch"))
            .Select(kv => CSharpSyntaxTree.ParseText(kv.Value))
            .Prepend(CSharpSyntaxTree.ParseText(Source));

        var compilation = CSharpCompilation.Create(
            "PatchConverterRuntime",
            trees,
            result.OutputCompilation.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine,
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));

        return Assembly.Load(stream.ToArray());
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<IEntity>(),
                GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
                GeneratorTestHelper.FromType<EntityAttribute>(),
                GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
                GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
                GeneratorTestHelper.FromType<Patch.PatchAttribute<object>>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
                GeneratorTestHelper.FromType<Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Result.Result<,>)),
                GeneratorTestHelper.FromType<Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(JsonSerializer)),
            ]);
    }
}
