using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>
/// Tests section path inference by running the generator with different class names
/// and verifying the generated binding section path.
/// </summary>
public class SectionPathInferenceTests : Generator.ConfigurationGeneratorTestBase
{
    [Theory]
    [InlineData("BookingOptions", "Booking")]
    [InlineData("PaymentOptions", "Payment")]
    [InlineData("MyConfig", "MyConfig")]
    public void InferSectionPath_FromClassName_MatchesExpected(string className, string expectedSection)
    {
        var source = $$"""
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public partial class {{className}}
            {
                public int Value { get; set; } = 42;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationSource = sources.Values.FirstOrDefault(s => s.Contains("GetSection"));
        registrationSource.Should().NotBeNull();
        registrationSource.Should().Contain($"GetSection(\"{expectedSection}\")");
    }
}
