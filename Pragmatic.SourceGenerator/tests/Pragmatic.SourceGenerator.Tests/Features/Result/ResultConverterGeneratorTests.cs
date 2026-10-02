using Microsoft.CodeAnalysis;
using Pragmatic.Result;
using Pragmatic.Result.Serialization;
using Pragmatic.Serialization;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Result;

/// <summary>
///     <c>[assembly: JsonResultContract&lt;…&gt;]</c> replaces <c>ResultJsonConverterFactory</c>, which
///     built converters with <c>MakeGenericType</c> + <c>Activator.CreateInstance</c> and reached the
///     result's factory methods through <c>MethodInfo.Invoke</c> — reflective on every runtime and never
///     AOT-safe.
/// </summary>
/// <remarks>
///     These assert that the generated code <b>compiles</b>, not only that it contains the right text.
///     A converter that reads well and does not build is the failure mode this repo has hit repeatedly.
/// </remarks>
public class ResultConverterGeneratorTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Result<,>)),
        GeneratorTestHelper.FromType<JsonResultContractAttribute<VoidResult>>(),
        GeneratorTestHelper.FromType<PragmaticJsonOptions>(),
        // The generated converter derives from JsonConverter<T>; without System.Text.Json in the test
        // compilation every one of its type names fails to bind and the "does it compile" assertion
        // reports the harness's gap as the generator's.
        GeneratorTestHelper.FromType<System.Text.Json.JsonSerializerOptions>()
    ];

    private const string Source = """
        using Pragmatic.Result;
        using Pragmatic.Result.Http;
        using Pragmatic.Result.Serialization;

        [assembly: JsonResultContract<Result<string, NotFoundError, ConflictError>>]
        [assembly: JsonResultContract<Result<int, NotFoundError>>]
        [assembly: JsonResultContract<VoidResult<NotFoundError, ConflictError>>]

        namespace TestApp;

        public sealed class Marker;
        """;

    private static string? GetFile(SourceGenRunResult result, string hintFragment)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains(hintFragment))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void JsonResultContract_MultiErrorResult_GeneratesAConverterThatCompiles()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var errors = string.Join(
            System.Environment.NewLine,
            GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()));
        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "a generated converter that does not compile takes the whole consuming assembly down: " + errors);

        GetFile(result, "ResultStringNotFoundErrorConflictErrorJsonConverter")
            .Should().NotBeNull("the multi-error variant has no fixed-arity converter to fall back on");
    }

    [Fact]
    public void JsonResultContract_MultiErrorResult_SwitchesOverTheDeclaredErrorSlots()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var converter = GetFile(result, "ResultStringNotFoundErrorConflictErrorJsonConverter");

        converter.Should().NotBeNull();
        // Reading never infers which of value/E1/E2 the payload holds: isSuccess says so, and the
        // error's own discriminator picks the slot. Both are compile-time constants here.
        converter!.Should()
            .Contain("case \"Pragmatic.Result.Http.NotFoundError\":")
            .And.Contain("case \"Pragmatic.Result.Http.ConflictError\":")
            .And.Contain("ErrorJson.ReadDiscriminator(errorElement)");
    }

    [Fact]
    public void JsonResultContract_MultiErrorResult_ConverterUsesNoReflection()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var converter = GetFile(result, "ResultStringNotFoundErrorConflictErrorJsonConverter");

        converter.Should().NotBeNull();
        converter!.Should()
            .NotContain("MakeGenericType")
            .And.NotContain("Activator.CreateInstance")
            .And.NotContain("GetMethod(")
            .And.NotContain("GetGenericArguments");
    }

    [Fact]
    public void JsonResultContract_TwoArityResult_ReusesTheExistingTypedConverter()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        // Result<T, E> already has a fixed-arity converter, so nothing is generated for it — the
        // registration just constructs the one that exists.
        GetFile(result, "ResultInt32NotFoundErrorJsonConverter").Should().BeNull();

        GetFile(result, "_Infra.Result.ResultConverters")!
            .Should().Contain("new global::Pragmatic.Result.Serialization.ResultJsonConverter<int, global::Pragmatic.Result.Http.NotFoundError>()");
    }

    [Fact]
    public void JsonResultContract_Declarations_RegisterOnPragmaticJsonOptions()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var registration = GetFile(result, "_Infra.Result.ResultConverters");

        registration.Should().NotBeNull("without a registration the converters exist and are never used");
        registration!.Should()
            .Contain("AddPragmaticResultConverters(this global::Pragmatic.Serialization.PragmaticJsonOptions json)")
            .And.Contain("json.AddConverter(new ResultStringNotFoundErrorConflictErrorJsonConverter());")
            .And.Contain("json.AddConverter(new VoidResultNotFoundErrorConflictErrorJsonConverter());");
    }

    [Fact]
    public void JsonResultContract_VoidVariant_HasNoValueBranch()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var converter = GetFile(result, "VoidResultNotFoundErrorConflictErrorJsonConverter");

        converter.Should().NotBeNull();
        // A void result carries nothing on success. Declaring the unused locals anyway is CS0219,
        // which this repo builds as an error.
        converter!.Should().NotContain("var hasValue = false;");
    }
}
