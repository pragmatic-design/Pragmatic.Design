using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Nested validatable properties, single and in a collection.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Which properties are selected is predicted: «does this type carry validation attributes?»,
///         the same question the generator that emits the validator asks. Asking the symbol whether it
///         implements <c>ISyncValidator</c> answers «no» for every type of the current compilation, because
///         that interface is added by another generator, whose output this one cannot see.
///     </para>
///     <para>
///         ⚠️ A <c>List&lt;T&gt;</c> is never an <c>ISyncValidator</c>: <c>T</c> is. A collection is judged
///         by its element type and walked.
///     </para>
/// </remarks>
public class TheValidatablesInACollectionTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Result;
        using Pragmatic.Validation.Attributes;

        namespace TestApp;

        public partial class LineDto
        {
            [GreaterThan(0)]
            public int Quantity { get; init; }
        }

        [DomainAction]
        public partial class SubmitOrder : DomainAction<int, IError>
        {
            public LineDto? Single { get; init; }
            public List<LineDto> Many { get; init; } = [];

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;

    [Fact]
    public void EachElementOfACollection_IsValidated()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "SubmitOrder.ValidationMetadata");
        metadata.Should().NotBeNull(
            "the nested type carries a validation attribute, so it will have a validator");

        metadata!.Should().Contain("foreach (var __element in Many)",
            "a List<T> is not an ISyncValidator: its elements are, and they are walked");

        // The control: the single property is still selected — without a cast either, because the
        // generator has already established that the type will have the validator (docs/CONVENTIONS.md,
        // «Decide at compile time»).
        metadata.Should().Contain("Single.Validate()");
        metadata.Should().NotContain("is ISyncValidator",
            "asking at run time what the generator knows leaves a branch that is never taken");
    }

    /// <summary>A type with no rules is not selected: the selection does not take everything.</summary>
    /// <remarks>
    ///     The predictor's control. Without it, a prediction that always answered «yes» — validating every
    ///     reference property — would go unnoticed, and the generated code would call <c>Validate()</c> on
    ///     types that do not have it.
    /// </remarks>
    [Fact]
    public void TheControl_ATypeWithNoRules_IsNotSelected()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source.Replace("[GreaterThan(0)]", ""), References);

        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "SubmitOrder.ValidationMetadata");

        (metadata ?? "").Should().NotContain("foreach (var __element in Many)",
            "with no rules the type will have no validator, and there is nothing to call");
    }

    /// <summary>
    ///     The asynchronous validator is resolved for the <b>element type</b> too.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Nobody registers a validator for <c>List&lt;LineDto&gt;</c>: resolving one would always return
    ///     null, so the code would run and never find anything — a silence harder to see than the
    ///     synchronous one, because the generated code looks as if it validated.
    /// </remarks>
    [Fact]
    public void TheAsyncValidator_IsResolvedForTheElementType()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source.Replace("[DomainAction]", "[DomainAction] [Validate]"),
            References);

        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "SubmitOrder.ValidationMetadata");
        metadata.Should().NotBeNull();

        metadata!.Should().Contain("IAsyncValidator<global::TestApp.LineDto>>()",
            "the validator belongs to the element, not to the list");
        metadata.Should().NotContain("IAsyncValidator<global::System.Collections.Generic.List<",
            "nobody registers a validator for List<T>, and asking for one always returns null");
        metadata.Should().Contain("foreach (var __element in Many)");
    }

    /// <summary>
    ///     ⚠️ The measure of whether <c>ValidateNestedSync</c> serves anyone.
    /// </summary>
    /// <remarks>
    ///     The validator the Validation feature generates already walks the elements of a validatable
    ///     collection, with the index in the error path. If it does so for a <c>DomainAction</c> too — not
    ///     only for a mutation — Actions' nested validation would be a second mechanism for a solved problem.
    /// </remarks>
    [Fact]
    public void WhenTheParentHasRulesOfItsOwn_ItsValidatorWalksTheCollection()
    {
        // Any rule on the parent, and the validator appears.
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source.Replace("public List<LineDto> Many", "[Count(1, 99)]\n            public List<LineDto> Many"),
            References);

        var validator = GeneratorTestHelper.GetGeneratedSource(result, "SubmitOrder.Validator");
        validator.Should().NotBeNull();
        validator!.Should().Contain("Many[i].Validate()",
            "the parent's validator walks the elements itself, with the index in the path");
    }

    /// <summary>
    ///     ⚠️ And when the parent has **no** rules of its own, that validator does not exist at all.
    /// </summary>
    /// <remarks>
    ///     The measure that says Actions' nested validation is not a duplicate. The parent's validator is
    ///     generated only for a type that has something to validate **of its own** — the predicate looks
    ///     at that type's properties. A parent with no rules that nests children with rules receives
    ///     nothing, and without <c>ValidateNestedSync</c> nobody would check those children.
    /// </remarks>
    [Fact]
    public void WhenTheParentHasNoRules_OnlyTheNestedMetadataCoversTheChildren()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        GeneratorTestHelper.GetGeneratedSource(result, "SubmitOrder.Validator")
            .Should().BeNull("the parent has no rules of its own, so it has no validator");

        GeneratorTestHelper.GetGeneratedSource(result, "SubmitOrder.ValidationMetadata")
            .Should().NotBeNull("and this is what covers the children");
    }

    private static readonly Microsoft.CodeAnalysis.MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
        GeneratorTestHelper.FromType<Pragmatic.Validation.Attributes.GreaterThanAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Validation.ISyncValidator>(),
        GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
        GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
    ];
}
