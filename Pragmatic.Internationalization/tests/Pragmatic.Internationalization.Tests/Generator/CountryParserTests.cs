using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.SourceGenerator;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     Tests for the CountryParser used in source generation.
/// </summary>
public class CountryParserTests
{
    [Fact]
    public void Parse_SingleCountry_ReturnsCorrectModel()
    {
        var json = """
            [
              {
                "code": "US",
                "name": "United States",
                "nativeName": "United States",
                "defaultLanguage": "en",
                "defaultCurrency": "USD",
                "dateFormat": "M/d/yyyy",
                "decimalSeparator": ".",
                "groupSeparator": ","
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("US");
        result[0].Name.Should().Be("United States");
        result[0].NativeName.Should().Be("United States");
        result[0].DefaultLanguage.Should().Be("en");
        result[0].DefaultCurrency.Should().Be("USD");
        result[0].DateFormat.Should().Be("M/d/yyyy");
        result[0].DecimalSeparator.Should().Be('.');
        result[0].GroupSeparator.Should().Be(',');
    }

    [Fact]
    public void Parse_MultipleCountries_ReturnsAllModels()
    {
        var json = """
            [
              {
                "code": "US",
                "name": "United States",
                "nativeName": "United States",
                "defaultLanguage": "en",
                "defaultCurrency": "USD",
                "dateFormat": "M/d/yyyy",
                "decimalSeparator": ".",
                "groupSeparator": ","
              },
              {
                "code": "IT",
                "name": "Italy",
                "nativeName": "Italia",
                "defaultLanguage": "it",
                "defaultCurrency": "EUR",
                "dateFormat": "dd/MM/yyyy",
                "decimalSeparator": ",",
                "groupSeparator": "."
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().HaveCount(2);
        result.Should().Contain(c => c.Code == "US");
        result.Should().Contain(c => c.Code == "IT");
    }

    [Fact]
    public void Parse_EuropeanNumberFormat_ParsesSeparatorsCorrectly()
    {
        var json = """
            [
              {
                "code": "DE",
                "name": "Germany",
                "nativeName": "Deutschland",
                "defaultLanguage": "de",
                "defaultCurrency": "EUR",
                "dateFormat": "dd.MM.yyyy",
                "decimalSeparator": ",",
                "groupSeparator": "."
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].DecimalSeparator.Should().Be(',');
        result[0].GroupSeparator.Should().Be('.');
    }

    [Fact]
    public void Parse_SwissFormat_ParsesApostropheGroupSeparator()
    {
        var json = """
            [
              {
                "code": "CH",
                "name": "Switzerland",
                "nativeName": "Schweiz",
                "defaultLanguage": "de",
                "defaultCurrency": "CHF",
                "dateFormat": "dd.MM.yyyy",
                "decimalSeparator": ".",
                "groupSeparator": "'"
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].GroupSeparator.Should().Be('\'');
    }

    [Fact]
    public void Parse_SpecialCharactersInNativeName_PreservesCharacters()
    {
        var json = """
            [
              {
                "code": "JP",
                "name": "Japan",
                "nativeName": "日本",
                "defaultLanguage": "ja",
                "defaultCurrency": "JPY",
                "dateFormat": "yyyy/MM/dd",
                "decimalSeparator": ".",
                "groupSeparator": ","
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().HaveCount(1);
        result[0].NativeName.Should().Be("日本");
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyCollection()
    {
        var json = "[]";

        var result = CountryParser.Parse(json);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_IncompleteEntry_SkipsEntry()
    {
        // Entry missing dateFormat should be skipped
        var json = """
            [
              {
                "code": "US",
                "name": "United States",
                "nativeName": "United States",
                "defaultLanguage": "en",
                "defaultCurrency": "USD"
              },
              {
                "code": "IT",
                "name": "Italy",
                "nativeName": "Italia",
                "defaultLanguage": "it",
                "defaultCurrency": "EUR",
                "dateFormat": "dd/MM/yyyy",
                "decimalSeparator": ",",
                "groupSeparator": "."
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        // Only the complete entry should be present
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("IT");
    }

    [Fact]
    public void CountryModel_PropertyName_UsesShortFormForCommonCountries()
    {
        var json = """
            [
              {
                "code": "US",
                "name": "United States",
                "nativeName": "United States",
                "defaultLanguage": "en",
                "defaultCurrency": "USD",
                "dateFormat": "M/d/yyyy",
                "decimalSeparator": ".",
                "groupSeparator": ","
              },
              {
                "code": "GB",
                "name": "United Kingdom",
                "nativeName": "United Kingdom",
                "defaultLanguage": "en",
                "defaultCurrency": "GBP",
                "dateFormat": "dd/MM/yyyy",
                "decimalSeparator": ".",
                "groupSeparator": ","
              },
              {
                "code": "AE",
                "name": "United Arab Emirates",
                "nativeName": "الإمارات",
                "defaultLanguage": "ar",
                "defaultCurrency": "AED",
                "dateFormat": "dd/MM/yyyy",
                "decimalSeparator": ".",
                "groupSeparator": ","
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().Contain(c => c.PropertyName == "US");
        result.Should().Contain(c => c.PropertyName == "UK");  // GB -> UK
        result.Should().Contain(c => c.PropertyName == "UAE"); // AE -> UAE
    }

    [Fact]
    public void CountryModel_PropertyName_UsesFullNameForOtherCountries()
    {
        var json = """
            [
              {
                "code": "IT",
                "name": "Italy",
                "nativeName": "Italia",
                "defaultLanguage": "it",
                "defaultCurrency": "EUR",
                "dateFormat": "dd/MM/yyyy",
                "decimalSeparator": ",",
                "groupSeparator": "."
              },
              {
                "code": "NZ",
                "name": "New Zealand",
                "nativeName": "New Zealand",
                "defaultLanguage": "en",
                "defaultCurrency": "NZD",
                "dateFormat": "d/MM/yyyy",
                "decimalSeparator": ".",
                "groupSeparator": ","
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result.Should().Contain(c => c.PropertyName == "Italy");
        result.Should().Contain(c => c.PropertyName == "NewZealand");
    }

    [Fact]
    public void CountryModel_EscapedGroupSeparator_EscapesApostrophe()
    {
        var json = """
            [
              {
                "code": "CH",
                "name": "Switzerland",
                "nativeName": "Schweiz",
                "defaultLanguage": "de",
                "defaultCurrency": "CHF",
                "dateFormat": "dd.MM.yyyy",
                "decimalSeparator": ".",
                "groupSeparator": "'"
              }
            ]
            """;

        var result = CountryParser.Parse(json);

        result[0].EscapedGroupSeparator.Should().Be("\\'");
    }
}
