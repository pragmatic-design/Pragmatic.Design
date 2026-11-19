using Microsoft.CodeAnalysis;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Result;

/// <summary>
///     A C# 15 union that implements <c>IError</c> gets its <c>IError</c> members, each delegated to the active case (#865); and the two shapes
///     that cannot get them say why.
/// </summary>
/// <remarks>
///     <para>
///         These tests parse with Roslyn 4.14, the version the generator is built against, which cannot read <c>union</c>. So the union here is
///         the struct the compiler turns a union into: it implements <c>IUnion</c>, and each case is a one-parameter constructor. The generator
///         reads a union by exactly that shape (option A of docs/design/csharp15-union-errors.md), so the two are the same input to it.
///     </para>
///     <para>
///         The <c>union</c> keyword itself is compiled by the real C# 15 compiler in <c>Pragmatic.Integration.Tests</c> on <c>net11.0</c>
///         (<c>AUnionErrorSetAnswersWithItsCaseTests</c>), whose host answers each case with its own status over HTTP.
///     </para>
/// </remarks>
public class AUnionErrorSetTests
{
    // The runtime's IUnion is in net11.0's System.Runtime, which this net10.0 test does not reference.
    private const string UnionInterface = """
        namespace System.Runtime.CompilerServices
        {
            public interface IUnion { object? Value { get; } }
        }
        """;

    private const string Cases = """
        namespace Shop
        {
            public sealed record OrderLocked : Pragmatic.Result.Error
            {
                public override string Code => "ORDER_LOCKED";
                public override int StatusCode => 423;
                public override string Title => "Locked";
            }

            public sealed record NotAnError(string Reason);
        }
        """;

    private static string AUnion(string declaration, string body = "") => UnionInterface + Cases + $$"""
        namespace Shop
        {
            {{declaration}} : System.Runtime.CompilerServices.IUnion, Pragmatic.Result.IError
            {
                public PlaceOrderError(Pragmatic.Result.Http.NotFoundError value) => Value = value;
                public PlaceOrderError(Pragmatic.Result.Http.ConflictError value) => Value = value;
                public PlaceOrderError(OrderLocked value) => Value = value;
                public object? Value { get; }
                {{body}}
            }
        }
        """;

    private static readonly MetadataReference[] ResultAssemblies =
    [
        GeneratorTestHelper.FromType<IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Error))
    ];

    private static SourceGenRunResult Run(string source) => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, ResultAssemblies);

    private static string? UnionError(SourceGenRunResult result) => GeneratorTestHelper.GetGeneratedSource(result, "PlaceOrderError.UnionError");

    [Fact]
    public void APartialUnionOfErrors_GetsEveryIErrorMember_DelegatedToItsCase()
    {
        var result = Run(AUnion("public partial struct PlaceOrderError"));

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty(
            "the union declares IError and writes none of its members: only the generated ones make it compile");
        var generated = UnionError(result);
        generated.Should().NotBeNull();
        generated!.Should().Contain("public string Code => PragmaticActiveCase.Code;");
        generated.Should().Contain("public int StatusCode => PragmaticActiveCase.StatusCode;");
        generated.Should().Contain("public string Title => PragmaticActiveCase.Title;",
            "IError's default Title is empty, and a union that kept it would answer with no title whatever its case says");
        generated.Should().Contain("public string? Description => PragmaticActiveCase.Description;");
        generated.Should().Contain("PragmaticActiveCase.WriteExtensions(extensions);");
        generated.Should().Contain("PragmaticActiveCase.WriteExtensions(extensions, resolver);");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG000").Should().BeEmpty();
    }

    [Fact]
    public void AMemberTheUserWrote_IsLeftToTheUser()
    {
        var result = Run(AUnion("public partial struct PlaceOrderError", "public string Title => \"Order not placed\";"));

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty("a second Title would not compile");
        var generated = UnionError(result)!;
        generated.Should().NotContain("public string Title");
        generated.Should().Contain("public string Code => PragmaticActiveCase.Code;");
    }

    [Fact]
    public void AUnionThatIsNotPartial_IsPrag0003_AndGetsNothing()
    {
        var result = Run(AUnion("public struct PlaceOrderError"));

        var reported = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0003").ToList();
        reported.Should().HaveCount(1);
        reported[0].GetMessage().Should().Contain("partial union PlaceOrderError");
        UnionError(result).Should().BeNull("the generator cannot add a part to a union that is not partial");
    }

    [Fact]
    public void AUnionWithACaseThatIsNotAnError_IsPrag0004_AndGetsNothing()
    {
        var source = AUnion("public partial struct PlaceOrderError")
            .Replace("public PlaceOrderError(OrderLocked value) => Value = value;", "public PlaceOrderError(NotAnError value) => Value = value;");

        var result = Run(source);

        var reported = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0004").ToList();
        reported.Should().HaveCount(1);
        reported[0].Severity.Should().Be(DiagnosticSeverity.Error);
        reported[0].GetMessage().Should().Contain("'NotAnError'");
        UnionError(result).Should().BeNull("every member delegates to the active case, and this case has no code or status to give");
    }

    /// <summary>The control: an error that is not a union gets nothing of this, and no diagnostic.</summary>
    [Fact]
    public void AnErrorThatIsNotAUnion_IsLeftAlone()
    {
        var result = Run(Cases);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.Should().NotContain(h => h.Contains("UnionError", StringComparison.Ordinal));
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG000").Should().BeEmpty();
    }
}
