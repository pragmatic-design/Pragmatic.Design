using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Redaction;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
///     An entry that carries a declared value but no message template is written as its masked values,
///     and the provider counts it.
/// </summary>
/// <remarks>
///     Without a template the message cannot be rendered from the masked values, so it is written as
///     <c>key=value</c> pairs. Nothing leaks, but the entry is shaped unlike every other one, and without
///     a counter nobody can tell how often that happens.
/// </remarks>
public class ARedactedEntryWithoutATemplateIsCountedTests
{
    private sealed record Secret(string Value);

    private sealed class Map : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
        {
            members = type == typeof(Secret) ? [new RedactedMember(nameof(Secret.Value), RedactionReason.NotLogged)] : [];
            return members.Count > 0;
        }
    }

    [Fact]
    public void AStateWithADeclaredValueAndNoTemplate_IsCounted()
    {
        using var provider = Provider();
        IReadOnlyList<KeyValuePair<string, object?>> state = [new("Who", new Secret("s3cr3t"))];

        provider.CreateLogger("Test").Log(LogLevel.Information, default, state, null, static (_, _) => "rendered by the caller");

        provider.GetMetrics().RedactedWithoutTemplate.Should().Be(1);
        provider.GetLogEntries().Single().Message.Should().NotContain("s3cr3t");
    }

    /// <summary>The control: a declared value logged through an ordinary template is not counted.</summary>
    [Fact]
    public void ADeclaredValueWithItsTemplate_IsNotCounted()
    {
        using var provider = Provider();

        provider.CreateLogger("Test").LogInformation("Saw {Who}", new Secret("s3cr3t"));

        provider.GetMetrics().RedactedWithoutTemplate.Should().Be(0);
    }

    private static PragmaticMemoryProvider Provider()
    {
        var config = new PragmaticProviderConfiguration();
        config.Privacy.EnableRedaction = false;
        return new PragmaticMemoryProvider("memory", config) { DeclaredRedactor = new DeclaredRedactor([new Map()]) };
    }
}
