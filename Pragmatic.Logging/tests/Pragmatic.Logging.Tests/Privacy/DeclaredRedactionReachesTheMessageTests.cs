using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Redaction;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
///     A member a type declared must not be logged stays out of the rendered message, not only out of
///     the structured properties.
/// </summary>
/// <remarks>
///     <para>
///         Through a real <see cref="ILogger" />, which renders the message from the template. The
///         message used to be rendered with the caller's formatter before declared redaction ran, and
///         redaction then masked only the properties: <c>Registered Customer { …, Email = jane@… }</c>
///         went out in clear beside a masked property.
///     </para>
///     <para>
///         <see cref="DeclaredRedactionReachesEveryProviderTests" /> could not see it: it builds the entry
///         by hand, with the template as the message, so the value was never in the text.
///     </para>
/// </remarks>
public class DeclaredRedactionReachesTheMessageTests
{
    private sealed record Customer(string Reference, string Email);

    private sealed record Order(int Id);

    private sealed class Map : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
        {
            members = type == typeof(Customer)
                ? [new RedactedMember(nameof(Customer.Email), RedactionReason.PersonalData, "Contact")]
                : [];
            return members.Count > 0;
        }
    }

    [Fact]
    public void JsonProvider_TheDeclaredMemberAppearsNowhereInTheLine()
    {
        var line = LogThroughJson(configure: null,
            logger => logger.LogInformation("Registered {Customer}", new Customer("C-42", "jane@example.com")));

        line.Should().NotContain("jane@example.com");
        Message(line).Should().Contain(PersonalDataPatterns.Mask).And.Contain("C-42");
    }

    /// <summary>
    ///     With structured properties off nothing is extracted, so masking the properties alone would not
    ///     even have reached the value.
    /// </summary>
    [Fact]
    public void WithStructuredPropertiesOff_TheMessageIsStillMasked()
    {
        var line = LogThroughJson(configure: config => config.IncludeStructuredProperties = false,
            logger => logger.LogInformation("Registered {Customer}", new Customer("C-42", "jane@example.com")));

        line.Should().NotContain("jane@example.com");
        Message(line).Should().Contain(PersonalDataPatterns.Mask);
    }

    /// <summary>
    ///     The control: a value whose type declared nothing is rendered by the caller's own formatter,
    ///     exactly as before. Only an entry that carries a declared value is rendered again.
    /// </summary>
    [Fact]
    public void AValueThatDeclaredNothing_IsRenderedByItsOwnFormatter()
    {
        var line = LogThroughJson(configure: null,
            logger => logger.LogInformation("Placed {Order} for {Amount}", new Order(7), 12.5m));

        Message(line).Should().Be("Placed Order { Id = 7 } for 12.5");
    }

    private static string LogThroughJson(Action<PragmaticProviderConfiguration>? configure, Action<ILogger> log)
    {
        var config = PragmaticJsonConfiguration.ForJson();
        configure?.Invoke(config);

        var output = new MemoryStream();
        var provider = new PragmaticJsonProvider("json", config, output)
        {
            DeclaredRedactor = new DeclaredRedactor([new Map()]),
        };

        log(provider.CreateLogger("Test"));
        provider.Dispose();

        // The provider's StreamWriter writes a UTF-8 byte order mark before the first line.
        return Encoding.UTF8.GetString(output.ToArray()).Trim().TrimStart('﻿');
    }

    private static string Message(string line)
        => JsonDocument.Parse(line).RootElement.GetProperty("@message").GetString()!;
}
