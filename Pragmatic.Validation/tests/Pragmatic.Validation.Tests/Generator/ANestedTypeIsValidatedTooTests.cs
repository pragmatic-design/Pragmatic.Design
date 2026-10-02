using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     A validatable type declared inside another type.
/// </summary>
/// <remarks>
///     <para>
///         The generator reopens the enclosing types. A partial written at namespace level loses the
///         nesting: a request record declared inside the class that uses it produces <c>CS1527</c> for
///         the accessibility it can no longer have, and a run of <c>CS0050</c>/<c>CS0051</c>/<c>CS0057</c>
///         on the record members the compiler synthesises. Errors in a file the author cannot open,
///         naming members nobody wrote.
///     </para>
///     <para>
///         ⚠️ Reopening the enclosing types requires them to be <c>partial</c> as well, which is a
///         demand on the caller's code. Where it is not met the generator says so, rather than emitting
///         source that cannot compile for a second reason.
///     </para>
/// </remarks>
public class ANestedTypeIsValidatedTooTests : ValidationGeneratorTestBase
{
    /// <summary>Nested inside a partial type, the validator is generated inside it too.</summary>
    [Fact]
    public void NestedInAPartialType_IsGeneratedInsideItsContainer()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public static partial class Orders
                              {
                                  public sealed partial record CreateRequest
                                  {
                                      [Required]
                                      public string Email { get; init; } = "";
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("partial class Orders", "the container is reopened, not flattened away");
        generated.Should().Contain("string.IsNullOrEmpty(Email)");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Two levels deep, because one level can be got right by accident.
    /// </summary>
    [Fact]
    public void NestedTwoDeep_ReopensEveryContainer()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public static partial class Api
                              {
                                  public static partial class Orders
                                  {
                                      public sealed partial record CreateRequest
                                      {
                                          [Required]
                                          public string Email { get; init; } = "";
                                      }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("partial class Api");
        generated.Should().Contain("partial class Orders");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     A container that is not partial is refused with a message, not with broken source.
    /// </summary>
    /// <remarks>
    ///     Reopening it would need the <c>partial</c> modifier the author did not write, so the
    ///     generated file would fail to compile whatever it emitted. Saying which type to change is the
    ///     only useful output here.
    /// </remarks>
    [Fact]
    public void NestedInANonPartialType_ReportsPrag0221_AndEmitsNothing()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public static class Orders
                              {
                                  public sealed partial record CreateRequest
                                  {
                                      [Required]
                                      public string Email { get; init; } = "";
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0221").Should().BeTrue();
        GetGeneratedSource(result, "Validator").Should().BeNull(
            "emitting anything here would fail to compile, and the diagnostic already says why");
    }

    /// <summary>
    ///     The control: a top-level type keeps generating exactly what it did.
    /// </summary>
    /// <remarks>
    ///     Without it, "nesting works" is satisfied by a generator that wraps everything in a container
    ///     it invented, which would break every existing validatable type in the repository.
    /// </remarks>
    [Fact]
    public void ATopLevelType_IsStillGeneratedWithoutAContainer()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public sealed partial record CreateRequest
                              {
                                  [Required]
                                  public string Email { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("partial record CreateRequest");
        generated.Should().NotContain("partial class Orders");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0221").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }
}
