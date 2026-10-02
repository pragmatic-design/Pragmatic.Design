using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.EFCore;

public class LocalizedStringValueConverterTests
{
    [Fact]
    public void ConvertToProvider_SerializesTranslationsToJson()
    {
        var converter = new LocalizedStringValueConverter();
        var value = LocalizedString.From(("en", "Widget"), ("it", "Componente"));

        var json = (string)converter.ConvertToProvider(value)!;

        json.Should().Contain("\"en\":\"Widget\"").And.Contain("\"it\":\"Componente\"");
    }

    [Fact]
    public void ConvertFromProvider_DeserializesJsonToLocalizedString()
    {
        var converter = new LocalizedStringValueConverter();

        var value = (LocalizedString)converter.ConvertFromProvider(
            """{"en":"Widget","it":"Componente"}""")!;

        value.GetExact("en").Should().Be("Widget");
        value.GetExact("it").Should().Be("Componente");
    }

    [Fact]
    public void RoundTrip_PreservesAllTranslations()
    {
        var converter = new LocalizedStringValueConverter();
        var original = LocalizedString.From(("en", "Hello"), ("de", "Hallo"), ("it", "Ciao"));

        var json = (string)converter.ConvertToProvider(original)!;
        var restored = (LocalizedString)converter.ConvertFromProvider(json)!;

        // LocalizedString implements IEnumerable<KeyValuePair>, so the assertions route
        // .Should() to dictionary assertions (no .Be); compare via the type's own equality.
        restored.Equals(original).Should().BeTrue();
    }

    [Fact]
    public void ConvertFromProvider_EmptyString_ReturnsEmptyLocalizedString()
    {
        var converter = new LocalizedStringValueConverter();

        var value = (LocalizedString)converter.ConvertFromProvider("")!;

        value.IsEmpty.Should().BeTrue();
    }
}
