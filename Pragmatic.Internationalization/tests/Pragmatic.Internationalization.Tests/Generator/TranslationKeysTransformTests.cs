using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.I18n.Transforms;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     Tests for TranslationKeysTransform JSON parsing.
/// </summary>
public class TranslationKeysTransformTests
{
    [Fact]
    public void Parse_EmptyJson_ReturnsNull()
    {
        var result = TranslationKeysTransform.Parse("{}", "test.json");

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_SimpleKeys_ParsesRootKeys()
    {
        var json = """
                   {
                       "welcome": "Welcome!",
                       "goodbye": "Goodbye!"
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/translations/en.json");

        result.Should().NotBeNull();
        result!.RootKeys.Should().HaveCount(2);
        result.RootKeys.Should().Contain(k => k.FullKey == "welcome" && k.PropertyName == "Welcome");
        result.RootKeys.Should().Contain(k => k.FullKey == "goodbye" && k.PropertyName == "Goodbye");
    }

    [Fact]
    public void Parse_NestedKeys_CreatesGroups()
    {
        var json = """
                   {
                       "errors": {
                           "notFound": "Not found",
                           "accessDenied": "Access denied"
                       }
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/translations/en.json");

        result.Should().NotBeNull();
        result!.RootKeys.Should().BeEmpty();
        result.Groups.Should().HaveCount(1);

        var errorsGroup = result.Groups[0];
        errorsGroup.ClassName.Should().Be("Errors");
        errorsGroup.Keys.Should().HaveCount(2);
        errorsGroup.Keys.Should().Contain(k => k.FullKey == "errors.notFound");
        errorsGroup.Keys.Should().Contain(k => k.FullKey == "errors.accessDenied");
    }

    [Fact]
    public void Parse_DeeplyNestedKeys_CreatesNestedGroups()
    {
        var json = """
                   {
                       "errors": {
                           "validation": {
                               "required": "This field is required"
                           }
                       }
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/translations/en.json");

        result.Should().NotBeNull();
        result!.Groups.Should().HaveCount(1);

        var errorsGroup = result.Groups[0];
        errorsGroup.ClassName.Should().Be("Errors");
        errorsGroup.NestedGroups.Should().HaveCount(1);

        var validationGroup = errorsGroup.NestedGroups[0];
        validationGroup.ClassName.Should().Be("Validation");
        validationGroup.Keys.Should().ContainSingle(k => k.FullKey == "errors.validation.required");
    }

    [Fact]
    public void Parse_MixedKeys_HandlesCorrectly()
    {
        var json = """
                   {
                       "welcome": "Welcome!",
                       "buttons": {
                           "submit": "Submit",
                           "cancel": "Cancel"
                       }
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/translations/en.json");

        result.Should().NotBeNull();
        result!.RootKeys.Should().ContainSingle(k => k.FullKey == "welcome");
        result.Groups.Should().ContainSingle(g => g.ClassName == "Buttons");
    }

    [Fact]
    public void Parse_UnderscoreKeys_ConvertsToPascalCase()
    {
        var json = """
                   {
                       "welcome_message": "Welcome!",
                       "error_not_found": "Not found"
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/translations/en.json");

        result.Should().NotBeNull();
        result!.RootKeys.Should().Contain(k => k.PropertyName == "WelcomeMessage");
        result.RootKeys.Should().Contain(k => k.PropertyName == "ErrorNotFound");
    }

    [Fact]
    public void Parse_ClassNameFromPath_DerivesNamespace()
    {
        var result = TranslationKeysTransform.Parse(
            """{"test": "value"}""",
            "/MyApp.Web/translations/en.json");

        result.Should().NotBeNull();
        // Should derive namespace from path
        result!.Namespace.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_SampleValues_IncludesSampleFromJson()
    {
        var json = """
                   {
                       "greeting": "Hello, World!"
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/en.json");

        result.Should().NotBeNull();
        var key = result!.RootKeys.Single();
        key.SampleValue.Should().Be("Hello, World!");
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        var result = TranslationKeysTransform.Parse("not valid json", "test.json");

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_NullJson_ReturnsNull()
    {
        var result = TranslationKeysTransform.Parse(null!, "test.json");

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_ReservedKeyword_ConvertsToPascalCase()
    {
        // Note: After PascalCase conversion, "class" becomes "Class"
        // which is NOT a reserved keyword in C#, so no escaping needed
        var json = """
                   {
                       "class": "Class value",
                       "namespace": "Namespace value"
                   }
                   """;

        var result = TranslationKeysTransform.Parse(json, "/app/en.json");

        result.Should().NotBeNull();
        // PascalCase names like "Class" and "Namespace" are valid C# identifiers
        result!.RootKeys.Should().Contain(k => k.PropertyName == "Class");
        result.RootKeys.Should().Contain(k => k.PropertyName == "Namespace");
    }
}