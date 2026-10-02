using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.SourceGenerator;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     Tests for the LanguageParser used in source generation.
/// </summary>
public class LanguageParserTests
{
    [Fact]
    public void Parse_SingleLanguage_ReturnsCorrectModel()
    {
        var json = """
            [
              {
                "code": "en",
                "name": "English",
                "nativeName": "English",
                "rtl": false,
                "pluralFamily": "Germanic"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("en");
        result[0].Name.Should().Be("English");
        result[0].NativeName.Should().Be("English");
        result[0].IsRightToLeft.Should().BeFalse();
        result[0].PluralFamily.Should().Be("Germanic");
    }

    [Fact]
    public void Parse_MultipleLanguages_ReturnsAllModels()
    {
        var json = """
            [
              {
                "code": "en",
                "name": "English",
                "nativeName": "English",
                "rtl": false,
                "pluralFamily": "Germanic"
              },
              {
                "code": "it",
                "name": "Italian",
                "nativeName": "Italiano",
                "rtl": false,
                "pluralFamily": "Romance"
              },
              {
                "code": "ar",
                "name": "Arabic",
                "nativeName": "العربية",
                "rtl": true,
                "pluralFamily": "Arabic"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result.Should().HaveCount(3);
        result.Should().Contain(l => l.Code == "en");
        result.Should().Contain(l => l.Code == "it");
        result.Should().Contain(l => l.Code == "ar");
    }

    [Fact]
    public void Parse_RtlLanguage_SetsIsRightToLeftTrue()
    {
        var json = """
            [
              {
                "code": "ar",
                "name": "Arabic",
                "nativeName": "العربية",
                "rtl": true,
                "pluralFamily": "Arabic"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].IsRightToLeft.Should().BeTrue();
    }

    [Fact]
    public void Parse_SpecialCharactersInName_PreservesCharacters()
    {
        var json = """
            [
              {
                "code": "nb",
                "name": "Norwegian Bokmål",
                "nativeName": "Norsk bokmål",
                "rtl": false,
                "pluralFamily": "Germanic"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Norwegian Bokmål");
        result[0].NativeName.Should().Be("Norsk bokmål");
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyCollection()
    {
        var json = "[]";

        var result = LanguageParser.Parse(json);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_IncompleteEntry_SkipsEntry()
    {
        // Entry missing pluralFamily should be skipped
        var json = """
            [
              {
                "code": "en",
                "name": "English",
                "nativeName": "English",
                "rtl": false
              },
              {
                "code": "it",
                "name": "Italian",
                "nativeName": "Italiano",
                "rtl": false,
                "pluralFamily": "Romance"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        // Only the complete entry should be present
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("it");
    }

    [Fact]
    public void Parse_JsonWithWhitespace_ParsesCorrectly()
    {
        var json = """
            [
                {
                    "code"   :   "en"  ,
                    "name"   :   "English"  ,
                    "nativeName"   :   "English"  ,
                    "rtl"   :   false  ,
                    "pluralFamily"   :   "Germanic"
                }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("en");
    }

    [Fact]
    public void LanguageModel_PropertyName_ConvertsToPascalCase()
    {
        var json = """
            [
              {
                "code": "en",
                "name": "English",
                "nativeName": "English",
                "rtl": false,
                "pluralFamily": "Germanic"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result[0].PropertyName.Should().Be("English");
    }

    [Fact]
    public void LanguageModel_PropertyName_HandlesSpecialCases()
    {
        var json = """
            [
              {
                "code": "nb",
                "name": "Norwegian Bokmål",
                "nativeName": "Norsk bokmål",
                "rtl": false,
                "pluralFamily": "Germanic"
              },
              {
                "code": "nn",
                "name": "Norwegian Nynorsk",
                "nativeName": "Norsk nynorsk",
                "rtl": false,
                "pluralFamily": "Germanic"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result.Should().Contain(l => l.PropertyName == "NorwegianBokmal");
        result.Should().Contain(l => l.PropertyName == "NorwegianNynorsk");
    }

    [Fact]
    public void LanguageModel_EscapedName_EscapesQuotes()
    {
        var json = """
            [
              {
                "code": "test",
                "name": "Test \"Quoted\" Name",
                "nativeName": "Test",
                "rtl": false,
                "pluralFamily": "Other"
              }
            ]
            """;

        var result = LanguageParser.Parse(json);

        result[0].EscapedName.Should().Contain("\\\"");
    }
}
