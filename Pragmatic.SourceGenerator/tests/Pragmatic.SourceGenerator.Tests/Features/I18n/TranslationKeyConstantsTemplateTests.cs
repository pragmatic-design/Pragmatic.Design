using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Pragmatic.SourceGenerator.Features.I18n.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.I18n;

/// <summary>
///     <c>{ClassName}Keys</c>: every translation key as a constant, in the same hierarchy as
///     <c>{ClassName}</c>, so an attribute argument can name it.
/// </summary>
/// <remarks>
///     The typed keys are properties, and an attribute takes only constants — a validation
///     rule's <c>MessageKey</c> had to repeat the key as a string.
/// </remarks>
public class TranslationKeyConstantsTemplateTests
{
    [Fact]
    public void TheHintName_IsTheClassNameWithKeys()
        => new TranslationKeyConstantsTemplate(Model()).RenderOutput().HintName
            .Should().Be("MyApp.Resources.TKeys.g.cs");

    [Fact]
    public void ARootKey_IsAConstant()
        => new TranslationKeyConstantsTemplate(Model()).RenderOutput().Text
            .Should().Contain("public static class TKeys")
            .And.Contain("public const string Name = \"user.name\";");

    [Fact]
    public void ANestedKey_IsAConstantInTheSameHierarchy()
        => new TranslationKeyConstantsTemplate(Model()).RenderOutput().Text
            .Should().Contain("public static class Validation")
            .And.Contain("public static class LeaveRequest")
            .And.Contain("public const string EndsBeforeItStarts = \"validation.leave_request.ends_before_it_starts\";");

    /// <summary>The control: no keys, no class.</summary>
    [Fact]
    public void NoKeys_NoClass()
        => new TranslationKeyConstantsTemplate(new TranslationKeysGenerationModel(
                "MyApp.Resources", "T", [], [], ["en"], [], "", 0))
            .RenderOutput().Text.Length.Should().Be(0);

    private static TranslationKeysGenerationModel Model()
    {
        var leaveRequest = new TranslationKeyGroupModel(
            "LeaveRequest",
            [Key("validation.leave_request.ends_before_it_starts", "EndsBeforeItStarts")],
            []);
        var validation = new TranslationKeyGroupModel("Validation", [], [leaveRequest]);

        return new TranslationKeysGenerationModel(
            "MyApp.Resources", "T", [Key("user.name", "Name")], [validation],
            ImmutableArray.Create("en"), ImmutableArray.Create("en.json"), "translations/en.json", 2);
    }

    private static TranslationKeyModel Key(string fullKey, string propertyName) => new(
        fullKey, propertyName, ImmutableArray<string>.Empty,
        translations: ImmutableDictionary<string, string>.Empty);
}
