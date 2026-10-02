using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     <c>[ValidateElements]</c> in its bare form configures nothing, and is refused.
/// </summary>
/// <remarks>
///     <para>
///         The generator validates a collection's elements whenever the property is a collection
///         <b>and</b> its element type is an <c>ISyncValidator</c>
///         (<c>ValidatableTransform.Properties.cs:116</c>). That is exactly the attribute's own
///         precondition — <c>PRAG0205</c> refuses it when the element type is not one — so where the
///         attribute is legal it is redundant, and where it would add something it is already
///         refused.
///     </para>
///     <para>
///         ⚠️ <b>Removing the attribute changes nothing</b> on such a type — the generated validator is
///         the same walk. The one thing that does reach the template is <c>StopOnFirstError</c>.
///     </para>
///     <para>
///         The bare form is an error, not a documented marker and not an opt-out. A marker leaves a
///         form in the public surface that does nothing. An attribute called
///         <c>[ValidateElements]</c> that <em>declines</em> element validation is a name that lies, a
///         worse defect than the one it would fix. As an error, the attribute has exactly one
///         legitimate use and the compiler says which.
///     </para>
///     <para>
///         ⚠️ <b>The premise holds for the common case only.</b> A type whose <em>only</em>
///         annotation is <c>[ValidateElements]</c> gets no validator without it, because the
///         pipeline's entry gate reads attributes and <c>required</c> members. So the refusal is
///         restricted to types that carry other rules, where it really is redundant.
///         <see cref="AsTheOnlyAnnotationOnTheType_ItIsSilentAndGeneratesTheValidator" /> is the
///         control that keeps that boundary honest.
///     </para>
/// </remarks>
public class TheBareValidateElementsIsRefusedTests : ValidatorGeneratorTestBase
{
    [Fact]
    public void TheBareForm_IsRefused_AndNamesTheProperty()
    {
        var result = RunGenerator(Order("[ValidateElements]"));

        HasDiagnostic(result, "PRAG0223").Should().BeTrue(
            "the elements are walked because the element type is validatable, not because of this "
            + "attribute — so written bare it configures nothing");

        GetGeneratorDiagnostics(result)
            .Where(d => d.Id == "PRAG0223")
            .Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Should().Contain(message => message.Contains("Items", System.StringComparison.Ordinal),
                "the author has to be told which property, or the message sends them looking");
    }

    /// <summary>
    ///     The first control: with the one setting that reaches the template, it is silent.
    /// </summary>
    /// <remarks>
    ///     Without it, "the bare form is refused" is satisfied by refusing every form — which would
    ///     take <c>StopOnFirstError</c> with it, the only thing the attribute ever configured.
    /// </remarks>
    [Fact]
    public void WithStopOnFirstError_ItIsSilent()
    {
        var result = RunGenerator(Order("[ValidateElements(StopOnFirstError = true)]"));

        HasDiagnostic(result, "PRAG0223").Should().BeFalse(
            "StopOnFirstError reaches ValidatableTemplate and changes the loop — that is a real "
            + "setting and the attribute's one reachable purpose");
    }

    /// <summary>
    ///     ⚠️ The second control, and the one the decision turns on: <b>the walk is not the
    ///     attribute's doing</b>.
    /// </summary>
    /// <remarks>
    ///     A collection of validatable elements is walked with no attribute at all, and the error path
    ///     still carries the index. If this ever goes red, the premise of the whole issue is wrong and
    ///     the refusal above has to go with it — so it is here rather than in the suite next door.
    /// </remarks>
    [Fact]
    public void WithNoAttributeAtAll_TheElementsAreStillWalkedWithIndexedPaths()
    {
        var result = RunGenerator(Order(""));

        HasDiagnostic(result, "PRAG0223").Should().BeFalse("there is no attribute to refuse");

        var generated = GetGeneratedSource(result, "PlaceOrderRequest.Validator");

        generated.Should().NotBeNull();
        generated.Should().Contain("Items[",
            "the indexed path is what the attribute's own <remarks> claimed as its doing, and it is "
            + "there without it");
    }

    /// <summary>
    ///     The third control: on an element type that cannot be validated, the older refusal still
    ///     answers.
    /// </summary>
    /// <remarks>
    ///     PRAG0205 and PRAG0223 must not both fire, and the bare form on a non-validatable element is
    ///     PRAG0205's case — telling that author their attribute is redundant would be false, since
    ///     there is no walk at all.
    /// </remarks>
    [Fact]
    public void OnANonValidatableElement_ItIsStillTheOlderRefusal()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record PlaceOrderRequest
                              {
                                  [ValidateElements]
                                  public List<string> Tags { get; init; } = [];
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0205").Should().BeTrue("a string is no ISyncValidator");
        HasDiagnostic(result, "PRAG0223").Should().BeFalse(
            "nothing walks these elements, so calling the attribute redundant would be untrue");
    }

    /// <summary>
    ///     ⚠️ The fourth control, and the one that cost the issue its first premise: where the bare
    ///     attribute is the type's <b>only</b> annotation it is legal, and it is what produces the
    ///     validator.
    /// </summary>
    /// <remarks>
    ///     Two assertions, and the second is the point. Silence alone would be satisfied by a
    ///     generator that refuses nothing; what has to hold is that the elements are still validated —
    ///     so the attribute is carrying the type into the pipeline, which is a real effect and not the
    ///     redundancy PRAG0223 describes. Delete the attribute here and there is no validator at all,
    ///     which is how this was found.
    /// </remarks>
    [Fact]
    public void AsTheOnlyAnnotationOnTheType_ItIsSilentAndGeneratesTheValidator()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record OrderItemRequest
                              {
                                  [Required]
                                  public string ProductId { get; init; } = "";
                              }

                              public partial record PlaceOrderRequest
                              {
                                  [ValidateElements]
                                  public List<OrderItemRequest> Items { get; init; } = [];
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0223").Should().BeFalse(
            "nothing else annotates PlaceOrderRequest, so this attribute is the reason it has a "
            + "validator — calling it redundant would be advice that removes the validation");

        var generated = GetGeneratedSource(result, "PlaceOrderRequest.Validator");

        generated.Should().NotBeNull(
            "the entry gate reads attributes and required members: with none of either, the type "
            + "never reaches the generator");
        generated.Should().Contain("Items[", "and the elements are walked with indexed paths");
    }

    /// <summary>An order whose items are validatable, with whatever attribute the case is about.</summary>
    private static string Order(string attribute) =>
        $$"""
          using Pragmatic.Validation.Attributes;
          using System.Collections.Generic;

          namespace TestNamespace;

          public partial record OrderItemRequest
          {
              [Required]
              public string ProductId { get; init; } = "";

              [Range(1, 100)]
              public int Quantity { get; init; }
          }

          public partial record PlaceOrderRequest
          {
              [Required]
              {{attribute}}
              public List<OrderItemRequest> Items { get; init; } = [];
          }
          """;
}
