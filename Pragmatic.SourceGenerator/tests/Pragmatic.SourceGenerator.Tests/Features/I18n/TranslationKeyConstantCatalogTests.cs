using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.I18n;

/// <summary>
///     The constants' catalog answers the key a reference holds, however the reference is qualified.
/// </summary>
/// <remarks>
///     A rule reads a <c>TKeys</c> constant before the generator has written it, so the key
///     comes from here.
/// </remarks>
public class TranslationKeyConstantCatalogTests
{
    private static readonly TranslationKeyConstantCatalog Catalog = TranslationKeyConstantCatalog.Of(Model());

    [Theory]
    [InlineData("TKeys.Validation.LeaveRequest.EndsBeforeItStarts")]
    [InlineData("MyApp.Resources.TKeys.Validation.LeaveRequest.EndsBeforeItStarts")]
    [InlineData("global::MyApp.Resources.TKeys.Validation.LeaveRequest.EndsBeforeItStarts")]
    [InlineData("TKeys.Validation\n    .LeaveRequest.EndsBeforeItStarts")]
    public void AReference_IsTheKeyItsConstantHolds(string reference)
        => Catalog.KeyOf(reference).Should().Be("validation.leave_request.ends_before_it_starts");

    [Fact]
    public void ARootConstant_IsItsKey()
        => Catalog.KeyOf("TKeys.Name").Should().Be("user.name");

    [Fact]
    public void AMemberTheClassDoesNotHave_IsNoKey()
        => Catalog.KeyOf("TKeys.Validation.LeaveRequest.Nowhere").Should().BeNull();

    /// <summary>A group is a class, not a constant.</summary>
    [Fact]
    public void AGroup_IsNoKey()
        => Catalog.KeyOf("TKeys.Validation.LeaveRequest").Should().BeNull();

    [Fact]
    public void AReferenceToAnotherClass_IsNoKey()
        => Catalog.KeyOf("Messages.Validation.LeaveRequest.EndsBeforeItStarts").Should().BeNull();

    [Fact]
    public void NoTranslations_NoKeys()
        => TranslationKeyConstantCatalog.Of(null).KeyOf("TKeys.Name").Should().BeNull();

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
