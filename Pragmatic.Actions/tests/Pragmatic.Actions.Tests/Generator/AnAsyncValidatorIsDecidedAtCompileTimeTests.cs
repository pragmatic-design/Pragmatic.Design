using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Whether an operation runs an async validator is decided by the generator, the same way for a
///     mutation and for an action: a <c>[Validator]</c> for the operation, in its compilation, is the
///     opt-in, and <c>[Validate]</c> changes the default.
/// </summary>
/// <remarks>
///     <para>
///         Without generated metadata the runtime would have to ask the container for
///         <c>IAsyncValidator&lt;TMutation&gt;</c> on every call — the speculative lookup
///         <c>docs/CONVENTIONS.md</c> rules out — and a <c>[Validator]</c> for an action without
///         <c>[Validate]</c> would be registered and never called.
///     </para>
///     <para>
///         The generated metadata is what both runtimes read (<c>IActionValidationMetadata</c>), so
///         these assert on it; the executed proof for the action is <c>DeclaredAsyncValidatorTests</c>
///         in <c>Pragmatic.Integration.Tests</c>.
///     </para>
/// </remarks>
public class AnAsyncValidatorIsDecidedAtCompileTimeTests : ActionsGeneratorTestBase
{
    private const string Common = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        using Pragmatic.Validation;
        using Pragmatic.Validation.Attributes;
        using Pragmatic.Validation.Types;

        namespace TestApp
        {
            public class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Code { get; set; } = "";
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class CreateOrder : Mutation<Order>
            {
                public string Code { get; init; } = "";
            }

            [DomainAction]
            public partial class ShipOrder : DomainAction<bool>
            {
                public string Code { get; init; } = "";
                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
        }
        """;

    private const string MutationValidator = """

        namespace TestApp
        {
            [Validator]
            public sealed class CreateOrderValidator : IAsyncValidator<CreateOrder>
            {
                public Task<ValidationError> ValidateAsync(CreateOrder instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }
        }
        """;

    private const string ActionValidator = """

        namespace TestApp
        {
            [Validator]
            public sealed class ShipOrderValidator : IAsyncValidator<ShipOrder>
            {
                public Task<ValidationError> ValidateAsync(ShipOrder instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }
        }
        """;

    [Fact]
    public void AMutationWithoutAValidator_IsToldAtCompileTimeThatThereIsNone()
    {
        var metadata = Metadata(Common, "CreateOrder");

        metadata.Should().NotBeNull("a mutation carries its validation metadata, or the runtime has to look");
        metadata!.Should().Contain("IActionValidationMetadata.RunAsyncValidation => false");
    }

    [Fact]
    public void AMutationWithAValidator_RunsIt()
    {
        var metadata = Metadata(Common + MutationValidator, "CreateOrder");

        metadata.Should().NotBeNull();
        metadata!.Should().Contain("IActionValidationMetadata.RunAsyncValidation => true");
    }

    [Fact]
    public void AMutationWithNoValidation_SaysSo()
    {
        var metadata = Metadata(
            Common.Replace("[Mutation(Mode = MutationMode.Create)]", "[Mutation(Mode = MutationMode.Create)]\n    [NoValidation]"),
            "CreateOrder");

        metadata.Should().NotBeNull("[NoValidation] on a mutation is read by the runtime only through this metadata");
        metadata!.Should().Contain("IActionValidationMetadata.HasNoValidation => true");
    }

    /// <summary>
    ///     The shape the showcase uses: the validator is declared for the action's <c>Request</c>, not
    ///     for the action. Split from its validator so the control below is the same source minus one
    ///     declaration.
    /// </summary>
    private const string NestedRequest = """

        namespace TestApp
        {
            public partial class ShipOrderRequest
            {
                [NotEmpty]
                public string Address { get; init; } = "";
            }

            [DomainAction]
            public partial class ShipOrderWithRequest : DomainAction<bool>
            {
                public ShipOrderRequest Request { get; init; } = new();
                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
        }
        """;

    private const string NestedRequestValidator = """

        namespace TestApp
        {
            [Validator]
            public sealed class ShipOrderRequestValidator : IAsyncValidator<ShipOrderRequest>
            {
                public Task<ValidationError> ValidateAsync(ShipOrderRequest instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }
        }
        """;

    [Fact]
    public void AnActionWithAValidatorAndNoValidateAttribute_RunsIt()
    {
        var metadata = Metadata(Common + ActionValidator, "ShipOrder");

        metadata.Should().NotBeNull("the declared validator is the opt-in");
        metadata!.Should().Contain("IActionValidationMetadata.RunAsyncValidation => true");
    }

    /// <summary>
    ///     A validator declared for the nested request, and no <c>[Validate]</c> on the action: the
    ///     declaration is the opt-in there too, and it is the request's validator that gets resolved.
    /// </summary>
    [Fact]
    public void AnActionWhoseRequestHasAValidator_RunsIt_WithoutValidateOnTheAction()
    {
        var metadata = Metadata(Common + NestedRequest + NestedRequestValidator, "ShipOrderWithRequest");

        metadata.Should().NotBeNull("the validator declared for the request is the opt-in");
        metadata!.Should().Contain("IActionValidationMetadata.RunAsyncValidation => true");
        metadata.Should().Contain("IAsyncValidator<global::TestApp.ShipOrderRequest>");
    }

    /// <summary>
    ///     The control: the same action, minus the validator. The request still validates by attributes,
    ///     so the metadata is emitted — and says no async half, which is what tells the two apart.
    /// </summary>
    [Fact]
    public void AnActionWhoseRequestHasNoValidator_AsksForNone()
    {
        var metadata = Metadata(Common + NestedRequest, "ShipOrderWithRequest");

        metadata.Should().NotBeNull("a request that validates by attributes carries nested metadata");
        metadata!.Should().Contain("IActionValidationMetadata.RunAsyncValidation => false");
        metadata.Should().NotContain("IAsyncValidator<global::TestApp.ShipOrderRequest>");
    }

    /// <summary>
    ///     The control: an explicit <c>[Validate]</c> still decides, even against a validator. With every
    ///     value at its default the action gets no metadata, which the runtime reads as "no async" —
    ///     so the assertion is that nothing turned it on.
    /// </summary>
    [Fact]
    public void AnActionThatTurnsAsyncOff_KeepsItOff_EvenWithAValidator()
    {
        var metadata = Metadata(
            Common.Replace("[DomainAction]", "[DomainAction]\n    [Validate(Async = false)]") + ActionValidator,
            "ShipOrder");

        (metadata ?? "").Should().NotContain("IActionValidationMetadata.RunAsyncValidation => true");
    }

    /// <summary>The control: an action with no validator and no attribute is unchanged — no metadata.</summary>
    [Fact]
    public void AnActionWithoutAValidator_GetsNoMetadata()
    {
        var metadata = Metadata(Common, "ShipOrder");

        metadata.Should().BeNull();
    }

    private static string? Metadata(string source, string typeName)
    {
        var references = new List<MetadataReference>(GetActionsAndEntityReferences())
        {
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Validation.IAsyncValidator<>)),
        };
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references.ToArray());

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        return GetGeneratedSourcesAsDictionary(result)
            .Where(pair => pair.Key.Contains($".{typeName}.ValidationMetadata", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }
}
