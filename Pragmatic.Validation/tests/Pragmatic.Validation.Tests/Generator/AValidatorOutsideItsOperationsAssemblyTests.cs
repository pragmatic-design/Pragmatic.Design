using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     PRAG0215: an async validator for a mutation or a domain action, declared in another assembly
///     than the operation, is reported — it is registered and never called.
/// </summary>
/// <remarks>
///     Whether an operation runs its async validator is decided by the generator of the operation's
///     assembly, from the <c>[Validator]</c> classes it can see. One declared elsewhere is invisible to
///     it. The operation's assembly is compiled on its own here, so the validator reaches it only as a
///     reference — the only way to put a declaration outside the compilation the generator runs on.
///     The operation attributes are stubs with the real names: the check reads names, and this suite
///     does not reference <c>Pragmatic.Actions</c>.
/// </remarks>
public class AValidatorOutsideItsOperationsAssemblyTests : ValidationGeneratorTestBase
{
    private const string OperationLibrary = """
        namespace Pragmatic.Actions.Mutation
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MutationAttribute : System.Attribute { }
        }

        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ValidateAttribute : System.Attribute { }
        }

        namespace Orders
        {
            [Pragmatic.Actions.Mutation.Mutation]
            public class CreateOrder { public string Code { get; set; } = ""; }

            [Pragmatic.Actions.Mutation.Mutation]
            [Pragmatic.Actions.Attributes.Validate]
            public class RenameOrder { public string Code { get; set; } = ""; }

            public class OrderNote { public string Text { get; set; } = ""; }
        }
        """;

    private static readonly MetadataReference Operations =
        GeneratorTestHelper.CompileReference("OrdersModule", OperationLibrary);

    private static string ValidatorFor(string validatedType) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Validation;
        using Pragmatic.Validation.Attributes;
        using Pragmatic.Validation.Types;

        namespace Checks
        {
            [Validator]
            public sealed class TheValidator : IAsyncValidator<{{validatedType}}>
            {
                public Task<ValidationError> ValidateAsync({{validatedType}} instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }
        }
        """;

    [Fact]
    public void AValidatorForAMutationOfAnotherAssembly_IsReported()
    {
        var result = RunGenerator(ValidatorFor("Orders.CreateOrder"), Operations);

        var reported = GetDiagnosticsById(result, "PRAG0215").ToList();
        reported.Should().HaveCount(1);
        reported[0].GetMessage().Should().Contain("OrdersModule");
    }

    /// <summary>The control: <c>[Validate]</c> on the operation still resolves it from the container.</summary>
    [Fact]
    public void AValidatorForAnOperationThatDeclaresValidate_IsNotReported()
    {
        var result = RunGenerator(ValidatorFor("Orders.RenameOrder"), Operations);

        HasDiagnostic(result, "PRAG0215").Should().BeFalse();
    }

    /// <summary>The control: a type of another assembly that is not an operation is an ordinary validated type.</summary>
    [Fact]
    public void AValidatorForAPlainTypeOfAnotherAssembly_IsNotReported()
    {
        var result = RunGenerator(ValidatorFor("Orders.OrderNote"), Operations);

        HasDiagnostic(result, "PRAG0215").Should().BeFalse();
    }
}
