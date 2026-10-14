using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     An error's custom properties reach the problem details under the names every other body uses:
///     <c>JsonNamingPolicy.CamelCase</c>, which lowers the whole leading acronym, not only its first letter (#178).
/// </summary>
public class ErrorExtensionWireNameTests
{
    [Fact]
    public void AnAcronymLedProperty_IsWrittenUnderTheBodyNamingRule()
    {
        var extensions = new Dictionary<string, object?>();

        new AcronymLedError { URL = "https://example.test", IOStream = "stdin" }.WriteExtensions(extensions);

        extensions["url"].Should().Be("https://example.test");
        extensions["ioStream"].Should().Be("stdin");
        extensions.Should().NotContainKey("uRL");
        extensions.Should().NotContainKey("iOStream");
    }

    /// <summary>The rule is the serializer's own, not a copy of it.</summary>
    [Fact]
    public void TheKeys_AreWhatTheSerializersPolicyGives()
    {
        var extensions = new Dictionary<string, object?>();

        new AcronymLedError { URL = "u", IOStream = "s" }.WriteExtensions(extensions);

        extensions.Should().ContainKey(System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(nameof(AcronymLedError.URL)));
        extensions.Should().ContainKey(System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(nameof(AcronymLedError.IOStream)));
    }
}
