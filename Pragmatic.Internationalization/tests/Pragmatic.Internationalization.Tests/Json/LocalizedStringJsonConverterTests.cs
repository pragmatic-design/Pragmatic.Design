using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Json;

public class LocalizedStringJsonConverterTests
{
    [Fact]
    public void Read_Object_DeserializesCultures()
    {
        var json = "{\"en\":\"Hello\",\"it\":\"Ciao\"}";

        var result = JsonSerializer.Deserialize<LocalizedString>(json, CreateOptions())!;

        result.GetExact("en").Should().Be("Hello");
        result.GetExact("it").Should().Be("Ciao");
    }

    [Fact]
    public void Read_EmptyObject_ReturnsFreshMutableInstance_NotSharedEmpty()
    {
        // #13: deserializing {} must NOT return the shared frozen Empty singleton.
        var a = JsonSerializer.Deserialize<LocalizedString>("{}", CreateOptions())!;
        var b = JsonSerializer.Deserialize<LocalizedString>("{}", CreateOptions())!;

        a.IsEmpty.Should().BeTrue();
        ReferenceEquals(a, LocalizedString.Empty).Should().BeFalse();

        // Mutating one deserialized empty value must not affect the other, nor Empty.
        a.Set("en", "X");
        b.IsEmpty.Should().BeTrue();
        LocalizedString.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Read_NullValueForCulture_IsSkipped()
    {
        var json = "{\"en\":\"Hello\",\"it\":null}";

        var result = JsonSerializer.Deserialize<LocalizedString>(json, CreateOptions())!;

        result.HasCulture("en").Should().BeTrue();
        result.HasCulture("it").Should().BeFalse();
    }

    [Fact]
    public void Read_NonStringValue_ThrowsJsonException()
    {
        // #15: a numeric/object value must be a JsonException (→ 400), not InvalidOperationException.
        var json = "{\"en\":123}";

        var act = () => JsonSerializer.Deserialize<LocalizedString>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_NestedObjectValue_ThrowsJsonException()
    {
        var json = "{\"en\":{\"nested\":\"x\"}}";

        var act = () => JsonSerializer.Deserialize<LocalizedString>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new LocalizedStringJsonConverter());
        return options;
    }
}
